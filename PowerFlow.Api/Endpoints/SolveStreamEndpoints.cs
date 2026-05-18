using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

namespace PowerFlow.Api.Endpoints;

internal static class SolveStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static RouteGroupBuilder MapSolveStreamEndpoints(this RouteGroupBuilder group)
    {
        group
            .MapPost(
                "solve/stream",
                async (HttpContext context, SolveRequestDto request) =>
                {
                    var ct = context.RequestAborted;

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
                        context.Response.StatusCode = 422;
                        await context.Response.WriteAsJsonAsync(
                            new ValidationResultDto(false, [constructionError]),
                            JsonOpts,
                            ct
                        );
                        return;
                    }

                    var validation = NetworkValidator.Validate(network);
                    if (!validation.IsValid)
                    {
                        context.Response.StatusCode = 422;
                        await context.Response.WriteAsJsonAsync(validation.ToDto(), JsonOpts, ct);
                        return;
                    }

                    // Switch to SSE before any solver work so the client sees the
                    // content-type header immediately and can open its EventSource reader.
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers["X-Accel-Buffering"] = "no"; // disable Fly/nginx buffering

                    if (request.Options.Mode.Equals("DC", StringComparison.OrdinalIgnoreCase))
                    {
                        // DC is a single LU step — no iterations to stream. Jump straight
                        // to the result event.
                        var dc = await Task.Run(() => new DcPowerFlowSolver().Solve(network), ct);
                        await WriteSseAsync(
                            context.Response,
                            new ResultEventDto("result", dc.ToDto(network)),
                            ct
                        );
                        return;
                    }

                    // AC: pipe solver log messages → Channel → SSE events.
                    var channel = Channel.CreateUnbounded<string>(
                        new UnboundedChannelOptions { SingleWriter = true, SingleReader = true }
                    );

                    var logger = new SseLogger(channel.Writer, JsonOpts);
                    var solver = new NewtonRaphsonSolver
                    {
                        Log = logger,
                        Tolerance = request.Options.Tolerance,
                        MaxIterations = request.Options.MaxIterations,
                        FlatStart = request.Options.FlatStart,
                        EnforceLimits = request.Options.EnforceLimits,
                        DistributedSlack = request.Options.DistributedSlack,
                        WarmStartFromDc = request.Options.WarmStartFromDc,
                    };

                    // Run solver on thread-pool; complete the channel when done so the
                    // reader loop below exits cleanly.
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                var ac = solver.Solve(network);
                                var resultJson = JsonSerializer.Serialize(
                                    new ResultEventDto("result", ac.ToDto(network)),
                                    JsonOpts
                                );
                                channel.Writer.TryWrite(resultJson);
                            }
                            catch (Exception ex)
                            {
                                var errJson = JsonSerializer.Serialize(
                                    new ErrorEventDto("error", ex.Message),
                                    JsonOpts
                                );
                                channel.Writer.TryWrite(errJson);
                            }
                            finally
                            {
                                channel.Writer.Complete();
                            }
                        },
                        ct
                    );

                    // Drain the channel and forward each line as an SSE data frame.
                    await foreach (var line in channel.Reader.ReadAllAsync(ct))
                    {
                        await context.Response.WriteAsync($"data: {line}\n\n", ct);
                        await context.Response.Body.FlushAsync(ct);
                    }
                }
            )
            .RequireRateLimiting("solve");

        return group;
    }

    // ── SSE write helper ──────────────────────────────────────────────────────

    private static async Task WriteSseAsync<T>(
        HttpResponse response,
        T payload,
        CancellationToken ct
    )
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // ── ILogger implementation ────────────────────────────────────────────────

    // Intercepts the solver's ILogger calls, parses iteration and Q-limit-switch
    // messages, and writes structured SSE event JSON to the channel.
    private sealed class SseLogger(ChannelWriter<string> writer, JsonSerializerOptions opts)
        : ILogger
    {
        // Bus-type changes accumulate until the next iteration event flushes them.
        private readonly List<BusTypeChangeDto> _pending = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var msg = formatter(state, exception);

            if (TryParseIter(msg, out var iter, out var mismatch))
            {
                var changes = _pending.Count > 0 ? _pending.ToArray() : [];
                _pending.Clear();
                var json = JsonSerializer.Serialize(
                    new IterEventDto("iter", iter, mismatch, changes),
                    opts
                );
                writer.TryWrite(json);
                return;
            }

            if (TryParseQSwitch(msg, out var change))
            {
                _pending.Add(change);
            }
        }

        // ── Parsing helpers ───────────────────────────────────────────────────

        // Matches: "  iter   3  mismatch  1.2345e-03 pu"
        private static bool TryParseIter(string msg, out int iter, out double mismatch)
        {
            iter = 0;
            mismatch = 0;

            var s = msg.AsSpan().Trim();
            if (!s.StartsWith("iter "))
                return false;

            s = s["iter ".Length..].TrimStart();
            var space = s.IndexOf(' ');
            if (space < 0 || !int.TryParse(s[..space], out iter))
                return false;

            var mmIdx = msg.IndexOf("mismatch", StringComparison.Ordinal);
            if (mmIdx < 0)
                return false;

            var afterMm = msg.AsSpan(mmIdx + "mismatch".Length).TrimStart();
            var puIdx = afterMm.IndexOf(" pu");
            if (puIdx < 0)
                return false;

            return double.TryParse(
                afterMm[..puIdx],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out mismatch
            );
        }

        // Matches Q-limit switch messages:
        //   "  Q-limit: bus    4 PV→PQ  Qg=12.3 > Qmax=10.0 MVAr  [ceiling]"
        //   "  Q-limit: bus    4 PV→PQ  Qg=8.5 < Qmin=10.0 MVAr  [floor]"
        //   "  Q-limit: bus    4 PQ→PV  Vm=... [max recovered]"
        //   "  Q-limit: bus    4 PQ→PV  Vm=... [min recovered]"
        private static bool TryParseQSwitch(string msg, out BusTypeChangeDto change)
        {
            change = null!;
            if (!msg.Contains("Q-limit: bus"))
                return false;

            bool pvToPq = msg.Contains("PV→PQ");
            bool pqToPv = msg.Contains("PQ→PV");
            if (!pvToPq && !pqToPv)
                return false;

            // Extract bus ID between "bus " and the direction arrow.
            var busIdx = msg.IndexOf("bus ", StringComparison.Ordinal);
            if (busIdx < 0)
                return false;

            var afterBus = msg.AsSpan(busIdx + "bus ".Length).TrimStart();
            var arrow = afterBus.IndexOf('→');
            if (arrow < 2)
                return false; // "PV" or "PQ" prefix before "→" is 2 chars

            var idSpan = afterBus[..(arrow - 2)].TrimEnd();
            if (!int.TryParse(idSpan, out var busId))
                return false;

            string from,
                to,
                reason;
            if (pvToPq)
            {
                from = "PV";
                to = "PQ";
                reason = msg.Contains("[ceiling]") ? "Qmax" : "Qmin";
            }
            else
            {
                from = "PQ";
                to = "PV";
                reason = "Restored";
            }

            change = new BusTypeChangeDto(busId, from, to, reason);
            return true;
        }
    }
}
