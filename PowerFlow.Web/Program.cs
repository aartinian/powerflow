using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using PowerFlow.Web.Components;
using PowerFlow.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Trust the X-Forwarded-* headers Fly's edge proxy adds. Without this,
// HttpContext.Request.Scheme stays "http" behind the proxy even though
// the browser is on "https" — which breaks Blazor Server's circuit
// (wrong WebSocket scheme advertised, antiforgery cookie not Secure,
// SignalR fails to connect, button clicks/file uploads silently no-op).
//
// Clearing KnownNetworks/KnownProxies disables proxy IP allow-listing —
// safe here because the app only ever runs behind Fly's proxy in prod;
// the listening socket isn't exposed to the public internet directly.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Persist Data Protection keys in production so the antiforgery / cookie
// encryption key ring survives container restarts and Fly auto-stop/start
// cycles. Without this every restart invalidates all browser cookies and
// logs "key {…} not found in the key ring" on every page load.
//
// In Development we deliberately skip this: keys are ephemeral per-run,
// which is fine because you restart constantly anyway — and persisting
// them locally just produces stale-cookie warnings every time you restart.
if (!builder.Environment.IsDevelopment())
{
    var keysDir = Directory.Exists("/data")
        ? "/data/keys"
        : Path.Combine(builder.Environment.ContentRootPath, ".keys");
    Directory.CreateDirectory(keysDir);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("PowerFlow.Web");
}

var app = builder.Build();

// MUST run before anything that inspects scheme/host (auth, antiforgery,
// Blazor's URL building). Hence: very first middleware.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Unauthenticated liveness probe for Fly's health check. Defined before
// the auth middleware so it bypasses the password gate. Returns plain
// 200 OK — Fly's HTTP check needs 2xx, and `/` would otherwise 302 to
// the login page when PF_PASSWORD is set, failing the check.
app.MapGet("/healthz", () => Results.Ok("ok"))
   .AllowAnonymous();

// Single-shared-password gate. No-op unless PF_PASSWORD is set, so dev
// runs without auth. Sits ahead of static assets so even framework JS
// stays gated until the user authenticates.
app.UseMiddleware<SimpleAuthMiddleware>();

// Serve files from wwwroot directly via middleware. This is a belt-and-
// braces fallback to MapStaticAssets() below: in production on Fly the
// static-assets manifest can fail to load (the framework JS at
// /_framework/blazor.web.js was 404'ing, killing all Blazor interactivity).
// UseStaticFiles needs nothing but the on-disk file, so it always works
// for the bare-named files that ship in the publish output.
app.UseStaticFiles();

app.UseAntiforgery();

// Endpoint-based static assets: serves fingerprinted/manifest-only routes
// (e.g. /_framework/blazor.web.<hash>.js) when MapStaticAssets is healthy.
// Disk-backed paths are already handled by UseStaticFiles above.
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
