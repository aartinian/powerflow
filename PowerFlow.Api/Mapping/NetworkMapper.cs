using PowerFlow.Api.Dtos;
using PowerFlow.Core.Models;
using PowerFlow.Core.Solver;

namespace PowerFlow.Api.Mapping;

internal static class NetworkMapper
{
    // ── NetworkDto → PowerNetwork ────────────────────────────────────────────

    internal static PowerNetwork ToNetwork(this NetworkDto dto) =>
        new(
            dto.BaseMva,
            dto.Buses.Select(b => b.ToBus()),
            dto.Branches.Select(b => b.ToBranch()),
            dto.Generators.Select(g => g.ToGenerator())
        );

    private static Bus ToBus(this BusDto b) =>
        new(
            b.Id,
            ParseBusType(b.Type),
            b.Pd,
            b.Qd,
            b.Gs,
            b.Bs,
            b.Vm,
            b.Va,
            b.BaseKv,
            b.Vmax,
            b.Vmin
        );

    private static Branch ToBranch(this BranchDto b) =>
        // TapRatio is already normalised (0 → 1.0) when the DTO was produced from
        // the parser. Do NOT pass it through the Branch constructor's normalisation
        // again — Branch(tapRatio: 0) would re-map 0 → 1.0, corrupting real 0-tap entries.
        // We guarantee the DTO always carries a valid ratio ≥ 1.0.
        new(
            b.FromBusId,
            b.ToBusId,
            b.R,
            b.X,
            b.B,
            b.TapRatio,
            b.PhaseShift,
            b.RateA,
            b.IsInService,
            b.RateB,
            b.RateC,
            b.Angmin,
            b.Angmax
        );

    private static Generator ToGenerator(this GeneratorDto g) =>
        new(g.BusId, g.Pg, g.Qg, g.Qmax, g.Qmin, g.Vg, g.Pmax, g.Pmin, g.IsInService, g.MBase);

    private static BusType ParseBusType(string type) =>
        type switch
        {
            "PQ" => BusType.PQ,
            "PV" => BusType.PV,
            "Slack" => BusType.Slack,
            "Isolated" => BusType.Isolated,
            _ => throw new ArgumentException(
                $"Unknown bus type '{type}'. Expected PQ, PV, Slack, or Isolated."
            ),
        };

    // ── PowerNetwork → NetworkDto ────────────────────────────────────────────

    internal static NetworkDto ToDto(this PowerNetwork net, string? name = null) =>
        new(
            name,
            net.BaseMva,
            net.Buses.Select(b => b.ToDto()).ToArray(),
            net.Branches.Select((b, i) => b.ToDto(i)).ToArray(),
            net.Generators.Select((g, i) => g.ToDto(i)).ToArray()
        );

    private static BusDto ToDto(this Bus b) =>
        new(
            b.Id,
            FormatBusType(b.Type),
            b.Pd,
            b.Qd,
            b.Gs,
            b.Bs,
            b.Vm,
            b.Va,
            b.BaseKv,
            b.Vmax,
            b.Vmin
        );

    private static BranchDto ToDto(this Branch b, int index) =>
        new(
            index,
            b.FromBus,
            b.ToBus,
            b.R,
            b.X,
            b.B,
            b.TapRatio,
            b.PhaseShift,
            b.RateA,
            b.RateB,
            b.RateC,
            b.Angmin,
            b.Angmax,
            b.IsInService
        );

    private static GeneratorDto ToDto(this Generator g, int index) =>
        new(
            index,
            g.BusId,
            g.Pg,
            g.Qg,
            g.Qmax,
            g.Qmin,
            g.Vg,
            g.Pmax,
            g.Pmin,
            g.IsInService,
            g.MBase
        );

    private static string FormatBusType(BusType t) =>
        t switch
        {
            BusType.PQ => "PQ",
            BusType.PV => "PV",
            BusType.Slack => "Slack",
            BusType.Isolated => "Isolated",
            _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
        };

    // ── PowerFlowResult → SolveResultDto (AC) ────────────────────────────────

    internal static SolveResultDto ToDto(this PowerFlowResult r, PowerNetwork net)
    {
        var mva = net.BaseMva;

        var buses = net
            .Buses.Select(
                (b, i) =>
                    new SolvedBusDto(
                        BusId: b.Id,
                        Vm: r.Vm[i],
                        Va: r.Va[i],
                        Pg: r.Pg[i] * mva,
                        Qg: r.Qg[i] * mva,
                        Pd: b.Pd,
                        Qd: b.Qd
                    )
            )
            .ToArray();

        // Branch flows are ordered by in-service branches; recover the 0-based index
        // in the full (including out-of-service) branch list so the client can
        // correlate back to its NetworkDto.branches array.
        var branchFlows = MapBranchFlows(net, r.BranchFlows, mva);

        var generators = MapGenerators(net, r.Generators);

        var violations = r
            .VoltageViolations.Select(v => new VoltageViolationDto(
                v.BusId,
                v.Vm,
                v.IsOverVoltage,
                v.Vmin,
                v.Vmax,
                v.BaseKv,
                v.VmKv
            ))
            .ToArray();

        var balance = r.Balance is { } b2
            ? new SystemBalanceDto(
                b2.TotalGenerationMw,
                b2.TotalLoadMw,
                b2.TotalLossesMw,
                b2.LossPct,
                b2.TotalGenerationMvar,
                b2.TotalLoadMvar,
                b2.TotalShuntMvar,
                b2.TotalLossesMvar
            )
            : null;

        return new SolveResultDto(
            Mode: "AC",
            Converged: r.Converged,
            Iterations: r.Iterations,
            OuterIterations: r.OuterIterations,
            MaxMismatch: r.MaxMismatch,
            Lambda: r.Lambda != 0 ? r.Lambda : null,
            Buses: buses,
            Branches: branchFlows,
            Generators: generators,
            Violations: violations,
            Balance: balance
        );
    }

    // ── DcPowerFlowResult → SolveResultDto (DC) ──────────────────────────────

    internal static SolveResultDto ToDto(this DcPowerFlowResult r, PowerNetwork net)
    {
        var mva = net.BaseMva;

        var buses = net
            .Buses.Select(
                (b, i) =>
                    new SolvedBusDto(
                        BusId: b.Id,
                        Vm: null, // DC: Vm ≡ 1 pu by assumption — omit
                        Va: r.Va[i],
                        Pg: r.Pg[i] * mva,
                        Qg: null, // DC: no reactive
                        Pd: b.Pd,
                        Qd: null
                    )
            )
            .ToArray();

        // DC branch flows are real-power only, lossless (Pji = -Pij).
        var inSvcBranches = net
            .Branches.Select((b, i) => (Branch: b, Index: i))
            .Where(x => x.Branch.IsInService)
            .ToList();

        var branchFlows = r
            .BranchFlows.Select(
                (bf, pos) =>
                {
                    var fullIndex = pos < inSvcBranches.Count ? inSvcBranches[pos].Index : pos;
                    var rateA = pos < inSvcBranches.Count ? inSvcBranches[pos].Branch.RateA : 0.0;
                    double? loadingPct = rateA > 0 ? Math.Abs(bf.P) * mva / rateA * 100.0 : null;
                    return new SolvedBranchDto(
                        BranchIndex: fullIndex,
                        FromBusId: bf.FromBus,
                        ToBusId: bf.ToBus,
                        Pij: bf.P * mva,
                        Qij: null,
                        Pji: null, // lossless — omit to keep the payload clean
                        Qji: null,
                        LossMw: null,
                        LoadingPct: loadingPct
                    );
                }
            )
            .ToArray();

        return new SolveResultDto(
            Mode: "DC",
            Converged: true,
            Iterations: 1,
            OuterIterations: 1,
            MaxMismatch: 0,
            Lambda: null,
            Buses: buses,
            Branches: branchFlows,
            Generators: [],
            Violations: [],
            Balance: null
        );
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static SolvedBranchDto[] MapBranchFlows(
        PowerNetwork net,
        IReadOnlyList<BranchFlow> flows,
        double mva
    )
    {
        var inSvcBranches = net
            .Branches.Select((b, i) => (Branch: b, Index: i))
            .Where(x => x.Branch.IsInService)
            .ToList();

        return flows
            .Select(
                (bf, pos) =>
                {
                    var fullIndex = pos < inSvcBranches.Count ? inSvcBranches[pos].Index : pos;
                    double? loadingPct = double.IsNaN(bf.LoadingPct) ? null : bf.LoadingPct;
                    return new SolvedBranchDto(
                        BranchIndex: fullIndex,
                        FromBusId: bf.FromBusId,
                        ToBusId: bf.ToBusId,
                        Pij: bf.Pij * mva,
                        Qij: bf.Qij * mva,
                        Pji: bf.Pji * mva,
                        Qji: bf.Qji * mva,
                        LossMw: (bf.Pij + bf.Pji) * mva,
                        LoadingPct: loadingPct
                    );
                }
            )
            .ToArray();
    }

    private static SolvedGeneratorDto[] MapGenerators(
        PowerNetwork net,
        IReadOnlyList<GeneratorResult> results
    )
    {
        // Core returns one GeneratorResult per in-service generator, in network order.
        // Walk in-service generators to pair each with its 0-based index in the full list.
        var output = new List<SolvedGeneratorDto>(results.Count);
        int ri = 0;
        for (int i = 0; i < net.Generators.Count && ri < results.Count; i++)
        {
            var gen = net.Generators[i];
            if (!gen.IsInService)
                continue;
            var gr = results[ri++];
            output.Add(
                new SolvedGeneratorDto(
                    Index: i,
                    BusId: gr.BusId,
                    Pg: gr.Pg,
                    Qg: gr.Qg,
                    IsAtQmax: gr.IsAtQmax,
                    IsAtQmin: gr.IsAtQmin
                )
            );
        }
        return [.. output];
    }
}
