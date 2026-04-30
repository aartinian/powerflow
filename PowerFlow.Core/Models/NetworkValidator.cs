namespace PowerFlow.Core.Models;

/// <summary>
/// Validates a <see cref="PowerNetwork"/> before it is passed to the solver.
/// Call <see cref="Validate"/> and inspect the returned <see cref="ValidationResult"/>;
/// or call <c>Validate(net).ThrowIfInvalid()</c> for a fire-and-forget guard.
/// </summary>
public static class NetworkValidator
{
    public static ValidationResult Validate(PowerNetwork network)
    {
        var errors = new List<ValidationError>();
        var busIds = network.Buses.Select(b => b.Id).ToHashSet();

        CheckSlackBuses(network, errors);
        CheckBranchBusRefs(network, busIds, errors);
        CheckGeneratorBusRefs(network, busIds, errors);
        CheckInServiceBranches(network, errors);
        CheckBusVoltageLimits(network, errors);
        CheckBranchParameters(network, errors);

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
                        $"Branch {br.FromBus}→{br.ToBus}: tap ratio {br.TapRatio:F4} is not positive. "
                            + "This will cause division by zero in the Y-bus."
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
}
