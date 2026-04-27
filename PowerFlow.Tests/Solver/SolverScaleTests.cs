using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Convergence and voltage accuracy tests for IEEE 30-bus, 57-bus, and 118-bus cases.
/// Reference voltages taken from the MATPOWER case files (runpf solution stored in bus data).
/// </summary>
public class SolverScaleTests
{
    // ─── IEEE 30-bus ────────────────────────────────────────────────────────────

    // case30.m already stores Vm=1, Va=0 for all buses and Vg=1 for all generators;
    // it is effectively a flat-start case and can be used directly.
    [Fact]
    public void Solve_Case30_Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case30.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
        Assert.True(
            result.Iterations <= 15,
            $"Took {result.Iterations} iterations (expected ≤ 15)"
        );
    }

    // ─── IEEE 57-bus ────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_Case57_FlatStart_Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case57_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
        Assert.True(
            result.Iterations <= 15,
            $"Took {result.Iterations} iterations (expected ≤ 15)"
        );
    }

    // Bus indices are 0-based. Slack is bus 1 (Va=0 in case57), so no angle offset.
    [Theory]
    [InlineData(18, 0.970)] // bus 19 — PQ
    [InlineData(29, 0.962)] // bus 30 — PQ
    [InlineData(56, 0.965)] // bus 57 — PQ (tip bus)
    public void Solve_Case57_FlatStart_VoltageMagnitude(int busIdx, double vmRef)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case57_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(vmRef, result.Vm[busIdx], 2); // ±0.005 pu (reference values stored to 3 dp)
    }

    [Theory]
    [InlineData(18, -13.20)] // bus 19
    [InlineData(29, -18.68)] // bus 30
    [InlineData(56, -16.56)] // bus 57
    public void Solve_Case57_FlatStart_VoltageAngle(int busIdx, double vaRef)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case57_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(vaRef, result.Va[busIdx], 1); // ±0.05°
    }

    // ─── IEEE 118-bus ───────────────────────────────────────────────────────────

    [Fact]
    public void Solve_Case118_FlatStart_Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case118_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
        Assert.True(
            result.Iterations <= 15,
            $"Took {result.Iterations} iterations (expected ≤ 15)"
        );
    }

    // Vm is independent of the slack angle reference, so no offset needed.
    [Theory]
    [InlineData(19, 0.958)] // bus 20 — PQ
    [InlineData(49, 1.001)] // bus 50 — PQ
    [InlineData(117, 0.949)] // bus 118 — PQ (tip bus)
    public void Solve_Case118_FlatStart_VoltageMagnitude(int busIdx, double vmRef)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case118_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(vmRef, result.Vm[busIdx], 2); // ±0.005 pu (reference values stored to 3 dp)
    }
}
