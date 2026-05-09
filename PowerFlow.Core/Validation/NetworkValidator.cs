using PowerFlow.Core.Models;

namespace PowerFlow.Core.Validation;

/// <summary>
/// Validates a <see cref="PowerNetwork"/> before it is passed to the solver.
/// Call <see cref="Validate"/> and inspect the returned <see cref="ValidationResult"/>;
/// or call <c>Validate(net).ThrowIfInvalid()</c> for a fire-and-forget guard.
/// </summary>
public static class NetworkValidator
{
    /// <summary>
    /// Validates the structure and parameters of <paramref name="network"/>.
    /// Returns <see cref="ValidationResult.Ok"/> when no issues are found.
    /// Errors indicate problems that will cause the solver to fail; warnings
    /// indicate suspicious data that may still produce a result.
    /// </summary>
    public static ValidationResult Validate(PowerNetwork network)
    {
        var errors = new List<ValidationError>();
        var busIds = network.Buses.Select(b => b.Id).ToHashSet();

        CheckSlackBuses(network, errors);
        CheckBranchBusRefs(network, busIds, errors);
        CheckGeneratorBusRefs(network, busIds, errors);
        CheckInServiceBranches(network, errors);
        CheckConnectivity(network, errors);
        CheckBusVoltageLimits(network, errors);
        CheckBranchParameters(network, errors);
        CheckGeneratorPLimits(network, errors);
        CheckMultipleVgAtBus(network, errors);

        return errors.Count == 0 ? ValidationResult.Ok : new ValidationResult(errors);
    }

    // ── Individual checks ────────────────────────────────────────────────────

    private static void CheckSlackBuses(PowerNetwork network, List<ValidationError> errors)
    {
        var slackBuses = network.Buses.Where(b => b.Type == BusType.Slack).ToList();

        if (slackBuses.Count == 0)
        {
            errors.Add(
                new ValidationError(
                    "NO_SLACK_BUS",
                    "Network has no slack bus. Exactly one bus must have type Slack."
                )
            );
        }
        else if (slackBuses.Count > 1)
        {
            var ids = string.Join(", ", slackBuses.Select(b => b.Id));
            errors.Add(
                new ValidationError(
                    "MULTIPLE_SLACK_BUSES",
                    $"Network has {slackBuses.Count} slack buses ({ids}). Exactly one is required."
                )
            );
        }
    }

    private static void CheckBranchBusRefs(
        PowerNetwork network,
        HashSet<int> busIds,
        List<ValidationError> errors
    )
    {
        foreach (var br in network.Branches)
        {
            if (!busIds.Contains(br.FromBus))
                errors.Add(
                    new ValidationError(
                        "BRANCH_FROM_BUS_MISSING",
                        $"Branch {br.FromBus}→{br.ToBus}: from-bus {br.FromBus} not in bus list."
                    )
                );

            if (!busIds.Contains(br.ToBus))
                errors.Add(
                    new ValidationError(
                        "BRANCH_TO_BUS_MISSING",
                        $"Branch {br.FromBus}→{br.ToBus}: to-bus {br.ToBus} not in bus list."
                    )
                );
        }
    }

    private static void CheckGeneratorBusRefs(
        PowerNetwork network,
        HashSet<int> busIds,
        List<ValidationError> errors
    )
    {
        foreach (var gen in network.Generators)
        {
            if (!busIds.Contains(gen.BusId))
                errors.Add(
                    new ValidationError(
                        "GENERATOR_BUS_MISSING",
                        $"Generator references bus {gen.BusId} which is not in the bus list."
                    )
                );
        }
    }

    private static void CheckInServiceBranches(PowerNetwork network, List<ValidationError> errors)
    {
        // A single-bus network has no branches by definition — skip the check.
        if (network.Buses.Count > 1 && !network.Branches.Any(b => b.IsInService))
        {
            errors.Add(
                new ValidationError(
                    "NO_IN_SERVICE_BRANCHES",
                    "Network has no in-service branches. All buses are electrically isolated.",
                    ValidationSeverity.Warning
                )
            );
        }
    }

    /// <summary>
    /// Verifies that every non-isolated bus is reachable from the slack bus via
    /// in-service branches. Reports <c>NETWORK_ISLANDED</c> when one or more buses
    /// form a disconnected island; the Y-bus for such a network would be singular
    /// and the solver would fail or silently diverge.
    /// </summary>
    private static void CheckConnectivity(PowerNetwork network, List<ValidationError> errors)
    {
        // If there is no slack bus the NO_SLACK_BUS check has already fired;
        // a reachability check from a missing reference bus is meaningless.
        var slackBus = network.Buses.FirstOrDefault(b => b.Type == BusType.Slack);
        if (slackBus is null)
            return;

        // If no branches are in service, NO_IN_SERVICE_BRANCHES already fires as a
        // warning — adding NETWORK_ISLANDED on top would be redundant noise.
        if (!network.Branches.Any(b => b.IsInService))
            return;

        // Undirected adjacency list built from in-service branches whose endpoints are known.
        var adj = network.Buses.ToDictionary(b => b.Id, _ => new List<int>());
        foreach (var br in network.Branches)
        {
            if (!br.IsInService)
                continue;
            if (!adj.ContainsKey(br.FromBus) || !adj.ContainsKey(br.ToBus))
                continue; // missing endpoint already flagged by CheckBranchBusRefs
            adj[br.FromBus].Add(br.ToBus);
            adj[br.ToBus].Add(br.FromBus);
        }

        // BFS from the slack bus.
        var visited = new HashSet<int> { slackBus.Id };
        var queue = new Queue<int>();
        queue.Enqueue(slackBus.Id);
        while (queue.Count > 0)
        {
            foreach (var nb in adj[queue.Dequeue()])
                if (visited.Add(nb))
                    queue.Enqueue(nb);
        }

        // Non-isolated buses not reached by BFS are stranded in an island.
        var stranded = network
            .Buses.Where(b => b.Type != BusType.Isolated && !visited.Contains(b.Id))
            .Select(b => b.Id)
            .OrderBy(id => id)
            .ToList();

        if (stranded.Count == 0)
            return;

        var ids = string.Join(", ", stranded);
        errors.Add(
            new ValidationError(
                "NETWORK_ISLANDED",
                $"{stranded.Count} bus(es) unreachable from slack bus {slackBus.Id}: {ids}. "
                    + "Each island needs its own slack bus, or mark disconnected buses as "
                    + "type Isolated (4)."
            )
        );
    }

    private static void CheckBusVoltageLimits(PowerNetwork network, List<ValidationError> errors)
    {
        foreach (var bus in network.Buses)
        {
            if (bus.Vmin >= bus.Vmax)
                errors.Add(
                    new ValidationError(
                        "INVALID_VOLTAGE_LIMITS",
                        $"Bus {bus.Id}: Vmin {bus.Vmin:F3} pu ≥ Vmax {bus.Vmax:F3} pu.",
                        ValidationSeverity.Warning
                    )
                );
        }
    }

    private static void CheckBranchParameters(PowerNetwork network, List<ValidationError> errors)
    {
        foreach (var br in network.Branches.Where(b => b.IsInService))
        {
            if (br.TapRatio <= 0)
                errors.Add(
                    new ValidationError(
                        "INVALID_TAP_RATIO",
                        $"Branch {br.FromBus}→{br.ToBus}: tap ratio {br.TapRatio:F4} is negative. "
                            + "A zero tap ratio is normalised to 1.0 by the Branch constructor; "
                            + "a negative value is a data error and will corrupt the Y-bus."
                    )
                );

            if (Math.Abs(br.PhaseShift) > 90.0)
                errors.Add(
                    new ValidationError(
                        "PHASE_SHIFT_OUT_OF_RANGE",
                        $"Branch {br.FromBus}→{br.ToBus}: phase shift {br.PhaseShift:F1}° is outside ±90°.",
                        ValidationSeverity.Warning
                    )
                );
        }
    }

    /// <summary>
    /// Warns when an in-service generator's scheduled real-power output lies
    /// outside its declared capacity band [Pmin, Pmax]. The power-flow equations
    /// are still solvable with the given Pg, but the dispatch is operationally
    /// infeasible and may indicate a data error in the case file.
    /// Out-of-service generators are skipped; their Pg is irrelevant to the solve.
    /// </summary>
    private static void CheckGeneratorPLimits(PowerNetwork network, List<ValidationError> errors)
    {
        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            if (gen.Pg > gen.Pmax)
                errors.Add(
                    new ValidationError(
                        "GENERATOR_P_ABOVE_PMAX",
                        $"Generator at bus {gen.BusId}: Pg = {gen.Pg:F1} MW exceeds "
                            + $"Pmax = {gen.Pmax:F1} MW.",
                        ValidationSeverity.Warning
                    )
                );
            else if (gen.Pg < gen.Pmin)
                errors.Add(
                    new ValidationError(
                        "GENERATOR_P_BELOW_PMIN",
                        $"Generator at bus {gen.BusId}: Pg = {gen.Pg:F1} MW is below "
                            + $"Pmin = {gen.Pmin:F1} MW.",
                        ValidationSeverity.Warning
                    )
                );
        }
    }

    /// <summary>
    /// Warns when two or more in-service generators share a PV bus but declare
    /// different voltage setpoints (Vg). The solver applies a single Vm setpoint
    /// per bus (last-generator-wins), so one or more generators will not see their
    /// requested terminal voltage.
    /// </summary>
    private static void CheckMultipleVgAtBus(PowerNetwork network, List<ValidationError> errors)
    {
        var pvBusIds = network.Buses.Where(b => b.Type == BusType.PV).Select(b => b.Id).ToHashSet();

        var gensByBus = network
            .Generators.Where(g => g.IsInService && pvBusIds.Contains(g.BusId))
            .GroupBy(g => g.BusId);

        foreach (var group in gensByBus)
        {
            var vgs = group.Select(g => g.Vg).Distinct().ToList();
            if (vgs.Count > 1)
            {
                var vals = string.Join(", ", vgs.Select(v => $"{v:F4}"));
                errors.Add(
                    new ValidationError(
                        "MULTIPLE_VG_AT_BUS",
                        $"Bus {group.Key}: {group.Count()} generators with disagreeing Vg setpoints "
                            + $"({vals} pu). The solver applies a single Vm per bus; "
                            + "one generator's setpoint will be ignored.",
                        ValidationSeverity.Warning
                    )
                );
            }
        }
    }
}
