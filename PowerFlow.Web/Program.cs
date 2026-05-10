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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
