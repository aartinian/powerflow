using System.Collections.Concurrent;
using System.Reflection;
using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Parsing;

namespace PowerFlow.Api.Endpoints;

internal static class CasesEndpoints
{
    // Bundled IEEE cases — label and topology metadata kept in sync with the
    // embedded .m files. Metadata is hardcoded so the list endpoint is instant
    // without parsing each file on startup.
    private static readonly CaseMetaDto[] BundledCases =
    [
        new("case14", "IEEE 14-bus", 14, 20, 5),
        new("case30", "IEEE 30-bus", 30, 41, 6),
        new("case57", "IEEE 57-bus", 57, 80, 7),
        new("case118", "IEEE 118-bus", 118, 186, 54),
        new("case300", "IEEE 300-bus", 300, 411, 69),
    ];

    // Memoise parsed bundled cases. Embedded resources are immutable for the
    // lifetime of the process — no invalidation needed. case300 is the
    // expensive parse (~5 ms cold) and is hit on every page load that opens
    // a case picker; without caching the same work runs per request.
    private static readonly ConcurrentDictionary<string, NetworkDto> ParsedCache = new();

    internal static RouteGroupBuilder MapCasesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("cases", () => Results.Ok(BundledCases));

        group.MapGet(
            "cases/{id}",
            async (string id) =>
            {
                var meta = Array.Find(BundledCases, c => c.Id == id);
                if (meta is null)
                    return Results.NotFound();

                if (ParsedCache.TryGetValue(id, out var cached))
                    return Results.Ok(cached);

                var content = await ReadEmbeddedCaseAsync(id);
                if (content is null)
                    return Results.NotFound();

                try
                {
                    var network = MatpowerParser.Parse(content);
                    var dto = network.ToDto(name: meta.Label);
                    ParsedCache.TryAdd(id, dto);
                    return Results.Ok(dto);
                }
                catch (FormatException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 422);
                }
            }
        );

        group.MapPost(
            "cases/parse",
            (ParseRequestDto request) =>
            {
                try
                {
                    var network = MatpowerParser.Parse(request.Content);
                    var name = ExtractCaseName(request.Content);
                    return Results.Ok(network.ToDto(name: name));
                }
                catch (FormatException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 422);
                }
            }
        );

        return group;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string?> ReadEmbeddedCaseAsync(string id)
    {
        // Embedded resource name: PowerFlow.Api.Cases.<id>.m
        var name = $"PowerFlow.Api.Cases.{id}.m";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null)
            return null;

        await using (stream)
        using (var reader = new StreamReader(stream))
            return await reader.ReadToEndAsync();
    }

    // Extracts the case name from the MATPOWER function declaration line.
    // "function mpc = case14" → "case14". Returns null when the line is absent.
    private static string? ExtractCaseName(string content)
    {
        var line = content.AsSpan();
        var nl = line.IndexOfAny('\n', '\r');
        if (nl >= 0)
            line = line[..nl];

        // "function mpc = case14"
        var eq = line.LastIndexOf('=');
        if (eq < 0)
            return null;

        var name = line[(eq + 1)..].Trim();
        return name.IsEmpty ? null : name.ToString();
    }
}
