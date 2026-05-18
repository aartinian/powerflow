using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

namespace PowerFlow.Api.Endpoints;

internal static class ContingencyEndpoints
{
    internal static RouteGroupBuilder MapContingencyEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/contingency — N-1 branch contingency sweep.
        // Trips each in-service branch in turn and returns a ranked list of
        // post-contingency results, most severe first.
        group
            .MapPost(
                "contingency",
                async (SolveRequestDto request) =>
                {
                    PowerFlow.Core.Models.PowerNetwork baseNetwork;
                    try
                    {
                        baseNetwork = request.Network.ToNetwork();
                    }
                    catch (ArgumentException ex)
                    {
                        var constructionError = new ValidationErrorDto(
                            "DUPLICATE_BUS_ID",
                            ex.Message,
                            "Error"
                        );
                        return Results.UnprocessableEntity(
                            new ValidationResultDto(false, [constructionError])
                        );
                    }

                    var validation = NetworkValidator.Validate(baseNetwork);
                    if (!validation.IsValid)
                        return Results.UnprocessableEntity(validation.ToDto());

                    // Collect the in-service branch indices once — these are the
                    // contingencies we will screen.
                    var inSvcIndices = request
                        .Network.Branches.Select((b, i) => (Branch: b, Index: i))
                        .Where(x => x.Branch.IsInService)
                        .Select(x => x.Index)
                        .ToArray();

                    var results = new ContingencyResultDto[inSvcIndices.Length];

                    await Task.Run(() =>
                        Parallel.For(
                            0,
                            inSvcIndices.Length,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Environment.ProcessorCount,
                            },
                            i =>
                            {
                                var branchIdx = inSvcIndices[i];
                                results[i] = SolveContingency(
                                    request.Network,
                                    branchIdx,
                                    request.Options
                                );
                            }
                        )
                    );

                    // Sort: non-converged first, then by overload count desc,
                    // then by max loading pct desc. Most severe contingency at index 0.
                    var sorted = results
                        .OrderByDescending(r => !r.Converged)
                        .ThenByDescending(r => r.BranchOverloadCount)
                        .ThenByDescending(r => r.VoltageViolationCount)
                        .ThenByDescending(r => r.MaxLoadingPct ?? 0)
                        .ToArray();

                    return Results.Ok(sorted);
                }
            )
            .RequireRateLimiting("contingency")
            .WithRequestTimeout("contingency");

        return group;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ContingencyResultDto SolveContingency(
        NetworkDto baseDto,
        int branchIndex,
        SolveOptionsDto options
    )
    {
        var tripped = baseDto.Branches[branchIndex];

        // Record with-expression: create a modified branch array with one
        // branch tripped. The rest of the network is shared (records are immutable).
        var modifiedBranches = baseDto.Branches.ToArray();
        modifiedBranches[branchIndex] = tripped with { IsInService = false };

        var contingencyDto = baseDto with { Branches = modifiedBranches };
        var network = contingencyDto.ToNetwork();

        SolveResultDto result;
        bool converged;
        try
        {
            result = Solve(network, options);
            converged = result.Converged;
        }
        catch
        {
            // Solver threw (singular matrix etc.) — treat as non-converged.
            return new ContingencyResultDto(
                BranchIndex: branchIndex,
                FromBusId: tripped.FromBusId,
                ToBusId: tripped.ToBusId,
                Converged: false,
                MaxLoadingPct: null,
                VoltageViolationCount: 0,
                BranchOverloadCount: 0,
                OverloadedBranches: [],
                VoltageViolations: []
            );
        }

        if (!converged)
        {
            return new ContingencyResultDto(
                BranchIndex: branchIndex,
                FromBusId: tripped.FromBusId,
                ToBusId: tripped.ToBusId,
                Converged: false,
                MaxLoadingPct: null,
                VoltageViolationCount: 0,
                BranchOverloadCount: 0,
                OverloadedBranches: [],
                VoltageViolations: []
            );
        }

        var overloaded = result
            .Branches.Where(b => b.LoadingPct > 100)
            .Select(b => new OverloadDto(b.FromBusId, b.ToBusId, b.LoadingPct!.Value))
            .ToArray();

        var loadings = result.Branches.Select(b => b.LoadingPct).Where(p => p.HasValue);
        double? maxLoading = loadings.Any() ? loadings.Max() : null;

        return new ContingencyResultDto(
            BranchIndex: branchIndex,
            FromBusId: tripped.FromBusId,
            ToBusId: tripped.ToBusId,
            Converged: true,
            MaxLoadingPct: maxLoading,
            VoltageViolationCount: result.Violations.Length,
            BranchOverloadCount: overloaded.Length,
            OverloadedBranches: overloaded,
            VoltageViolations: result.Violations
        );
    }

    private static SolveResultDto Solve(
        PowerFlow.Core.Models.PowerNetwork network,
        SolveOptionsDto options
    )
    {
        if (options.Mode.Equals("DC", StringComparison.OrdinalIgnoreCase))
            return new DcPowerFlowSolver().Solve(network).ToDto(network);

        return new NewtonRaphsonSolver
        {
            Tolerance = options.Tolerance,
            MaxIterations = options.MaxIterations,
            FlatStart = options.FlatStart,
            EnforceLimits = options.EnforceLimits,
            DistributedSlack = options.DistributedSlack,
            WarmStartFromDc = options.WarmStartFromDc,
        }
            .Solve(network)
            .ToDto(network);
    }
}
