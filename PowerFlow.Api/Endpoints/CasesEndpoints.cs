namespace PowerFlow.Api.Endpoints;

internal static class CasesEndpoints
{
    internal static RouteGroupBuilder MapCasesEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/cases — list bundled IEEE cases
        // GET /api/cases/{id} — parse and return a bundled case as NetworkDto
        // POST /api/parse — upload .m file content, return NetworkDto
        // (implemented in the next commit)
        return group;
    }
}
