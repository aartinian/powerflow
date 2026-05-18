using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using PowerFlow.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// ── Body size cap ───────────────────────────────────────────────────────────
// Largest legitimate request is a case300 NetworkDto (~150 KB JSON) plus solve
// options. 5 MiB leaves headroom for hand-edited cases without making OOM-by-POST
// trivial. Reject earlier (default 30 MB) before the body is fully buffered.
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = 5L * 1024 * 1024;
});

// ── Rate limiting ───────────────────────────────────────────────────────────
// Partition by client IP (X-Forwarded-For aware via UseForwardedHeaders above).
// Three named policies — solve and contingency get the tightest budgets because
// each request is real compute, not just JSON shuffling.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.AddPolicy(
        "default",
        ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            )
    );
    opts.AddPolicy(
        "solve",
        ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            )
    );
    opts.AddPolicy(
        "contingency",
        ctx =>
            RateLimitPartition.GetConcurrencyLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 1, // one sweep in flight per IP
                    QueueLimit = 0,
                }
            )
    );
});

// ── Request timeouts ────────────────────────────────────────────────────────
// Drop the connection if the solver runs unreasonably long. Doesn't actually
// cancel the synchronous solver work (Core's solver has no CancellationToken),
// but frees the response and surfaces a clear 503 to the client.
builder.Services.AddRequestTimeouts(opts =>
{
    opts.DefaultPolicy = new() { Timeout = TimeSpan.FromSeconds(30) };
    opts.AddPolicy("contingency", TimeSpan.FromMinutes(2));
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseRequestTimeouts();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

var api = app.MapGroup("/api").RequireRateLimiting("default");
api.MapCasesEndpoints();
api.MapValidateEndpoints();
api.MapSolveStreamEndpoints(); // register before /api/solve so the more-specific route wins
api.MapSolveEndpoints();
api.MapContingencyEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
