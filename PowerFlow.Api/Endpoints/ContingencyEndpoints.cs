using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

namespace PowerFlow.Api.Endpoints;

internal static class ContingencyEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static RouteGroupBuilder MapContingencyEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/contingency — N-1 branch contingency sweep, streamed as SSE.
        //
        // Each `row` event carries one contingency result as it completes — the
        // server doesn't wait to sort the full list before emitting. A final
        // `complete` event closes the stream. The client accumulates rows and
        // sorts on display, so the user sees progress on every solver finish
        // instead of a blank screen for the whole sweep.
        group
            .MapPost(
                "contingency",
                async (HttpContext context, SolveRequestDto request) =>
                {
                    var ct = context.RequestAborted;

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
                        context.Response.StatusCode = 422;
                        await context.Response.WriteAsJsonAsync(
                            new ValidationResultDto(false, [constructionError]),
                            JsonOpts,
                            ct
                        );
                        return;
                    }

                    var validation = NetworkValidator.Validate(baseNetwork);
                    if (!validation.IsValid)
                    {
                        context.Response.StatusCode = 422;
                        await context.Response.WriteAsJsonAsync(validation.ToDto(), JsonOpts, ct);
                        return;
                    }

                    // Switch to SSE before any solver work so the client can
                    // open its EventSource reader immediately.
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers["X-Accel-Buffering"] = "no";

                    var inSvcIndices = request
                        .Network.Branches.Select((b, i) => (Branch: b, Index: i))
                        .Where(x => x.Branch.IsInService)
                        .Select(x => x.Index)
                        .ToArray();

                    // Single-reader channel: many worker threads write per-row
                    // JSON, the response writer drains them in order. Avoids
                    // serialising HttpResponse access from Parallel.For.
                    var channel = Channel.CreateUnbounded<string>(
                        new UnboundedChannelOptions { SingleReader = true }
                    );

                    var producer = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await Task.Run(() =>
                                    Parallel.For(
                                        0,
                                        inSvcIndices.Length,
                                        new ParallelOptions
                                        {
                                            MaxDegreeOfParallelism = Environment.ProcessorCount,
                                            CancellationToken = ct,
                                        },
                                        i =>
                                        {
                                            var row = SolveContingency(
                                                request.Network,
                                                inSvcIndices[i],
                                                request.Options
                                            );
                                            var json = JsonSerializer.Serialize(
                                                new { type = "row", row },
                                                JsonOpts
                                            );
                                            channel.Writer.TryWrite(json);
                                        }
                                    )
                                );
                                var done = JsonSerializer.Serialize(
                                    new { type = "complete", total = inSvcIndices.Length },
                                    JsonOpts
                                );
                                channel.Writer.TryWrite(done);
                            }
                            catch (OperationCanceledException)
                            {
                                // client disconnected — just stop writing
                            }
                            finally
                            {
                                channel.Writer.Complete();
                            }
                        },
                        ct
                    );

                    await foreach (var line in channel.Reader.ReadAllAsync(ct))
                    {
                        await context.Response.WriteAsync($"data: {line}\n\n", ct);
                        await context.Response.Body.FlushAsync(ct);
                    }
                    await producer.ConfigureAwait(false);
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
