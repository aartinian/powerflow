using System.Text.Json;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Regression tests: re-solving each MATPOWER case must reproduce the stored
/// fixture values to within NR convergence tolerance (1e-6).
///
/// Fixtures under PowerFlow.Tests/Fixtures/ store per-bus Vm/Va produced by
/// the solver from a flat start, converged to 1e-6.  The case14 solution
/// has additionally been validated digit-by-digit against MATPOWER 7.x in
/// <see cref="SolverValidationTests"/>.  The remaining cases are regression
/// baselines: any solver regression that shifts a bus voltage by more than
/// 1e-6 pu / 1e-5 ° will be caught here.
///
/// Tolerances: |ΔVm| ≤ 1e-6 pu, |ΔVa| ≤ 1e-5 °.
/// </summary>
public class MatpowerReferenceTests
{
    // ─── Fixture loading ────────────────────────────────────────────────────────

    private sealed record BusRef(int Id, double Vm, double Va);

    private static List<BusRef> LoadFixture(string caseName)
    {
        var path = TestData.FixturePath($"{caseName}.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var buses = doc.RootElement.GetProperty("buses");

        return buses
            .EnumerateArray()
            .Select(b => new BusRef(
                b.GetProperty("id").GetInt32(),
                b.GetProperty("vm").GetDouble(),
                b.GetProperty("va").GetDouble()
            ))
            .ToList();
    }

    // ─── Reference comparison ───────────────────────────────────────────────────

    [Theory]
    [InlineData("case14")]
    [InlineData("case30")]
    [InlineData("case57")]
    [InlineData("case118")]
    [InlineData("case300")]
    public void Solve_MatchesMatpowerReference_Vm(string caseName)
    {
        var (net, result, refs) = Solve(caseName);
        var byId = net.Buses.Select((b, i) => (b.Id, i)).ToDictionary(t => t.Id, t => t.i);

        foreach (var r in refs)
        {
            int i = byId[r.Id];
            Assert.True(
                Math.Abs(result.Vm[i] - r.Vm) <= 1e-6,
                $"{caseName} bus {r.Id}: Vm={result.Vm[i]:F8} pu, ref={r.Vm:F8} pu, "
                    + $"Δ={Math.Abs(result.Vm[i] - r.Vm):e2} (limit 1e-6)"
            );
        }
    }

    [Theory]
    [InlineData("case14")]
    [InlineData("case30")]
    [InlineData("case57")]
    [InlineData("case118")]
    [InlineData("case300")]
    public void Solve_MatchesMatpowerReference_Va(string caseName)
    {
        var (net, result, refs) = Solve(caseName);
        var byId = net.Buses.Select((b, i) => (b.Id, i)).ToDictionary(t => t.Id, t => t.i);

        foreach (var r in refs)
        {
            int i = byId[r.Id];
            Assert.True(
                Math.Abs(result.Va[i] - r.Va) <= 1e-5,
                $"{caseName} bus {r.Id}: Va={result.Va[i]:F7}°, ref={r.Va:F7}°, "
                    + $"Δ={Math.Abs(result.Va[i] - r.Va):e2} (limit 1e-5)"
            );
        }
    }

    [Theory]
    [InlineData("case14")]
    [InlineData("case30")]
    [InlineData("case57")]
    [InlineData("case118")]
    [InlineData("case300")]
    public void Solve_Converges(string caseName)
    {
        var (_, result, _) = Solve(caseName);
        Assert.True(
            result.Converged,
            $"{caseName}: did not converge — max mismatch {result.MaxMismatch:e3} pu"
        );
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static (
        PowerFlow.Core.Models.PowerNetwork net,
        PowerFlowResult result,
        List<BusRef> refs
    ) Solve(string caseName)
    {
        var net = MatpowerParser.ParseFile(TestData.Path($"{caseName}.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var refs = LoadFixture(caseName);
        return (net, result, refs);
    }
}
