using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
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

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

var api = app.MapGroup("/api");
api.MapCasesEndpoints();
api.MapValidateEndpoints();
api.MapSolveStreamEndpoints(); // register before /api/solve so the more-specific route wins
api.MapSolveEndpoints();
api.MapContingencyEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
