using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

namespace PowerFlow.Api.Endpoints;

internal static class SolveEndpoints
{
    internal static RouteGroupBuilder MapSolveEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost(
            "solve",
            async (SolveRequestDto request) =>
            {
                // Map DTO → domain, catching constructor-level errors.
                PowerFlow.Core.Models.PowerNetwork network;
                try
                {
                    network = request.Network.ToNetwork();
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

                // Validate before solving — surfaces structural problems with stable
                // error codes instead of cryptic solver exceptions.
                var validation = NetworkValidator.Validate(network);
                if (!validation.IsValid)
                    return Results.UnprocessableEntity(validation.ToDto());

                // Run on a thread-pool thread so the request pipeline stays responsive
                // for large cases (case300 ~100 ms).
                try
                {
                    var result = await Task.Run(() => Solve(network, request.Options));
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: 500);
                }
            }
        );

        return group;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SolveResultDto Solve(
        PowerFlow.Core.Models.PowerNetwork network,
        SolveOptionsDto options
    )
    {
        if (options.Mode.Equals("DC", StringComparison.OrdinalIgnoreCase))
        {
            var dc = new DcPowerFlowSolver().Solve(network);
            return dc.ToDto(network);
        }

        var solver = new NewtonRaphsonSolver
        {
            Tolerance = options.Tolerance,
            MaxIterations = options.MaxIterations,
            FlatStart = options.FlatStart,
            EnforceLimits = options.EnforceLimits,
            DistributedSlack = options.DistributedSlack,
            WarmStartFromDc = options.WarmStartFromDc,
        };

        var ac = solver.Solve(network);
        return ac.ToDto(network);
    }
}
