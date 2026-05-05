using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Tests for the backtracking line-search step limiter.
/// Well-conditioned cases always accept μ = 1 on the first try, so the primary
/// concern here is that (a) enabling/disabling the search never changes the
/// converged solution, and (b) the solver is robust with MaxStepHalvings = 0
/// (pure Newton) as well as the default.
/// </summary>
public class SolverStepLimitingTests
{
    // ── Disabling the line search must not break well-conditioned cases ────────

    [Fact]
    public void Solve_StepLimitingDisabled_Case14Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = 0 }.Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    [Fact]
    public void Solve_StepLimitingDisabled_Case118Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case118.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = 0 }.Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    [Fact]
    public void Solve_StepLimitingDisabled_Case300Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case300.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = 0 }.Solve(net);

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    // ── Enabling the search must not change the solution on easy cases ─────────
    // For well-conditioned networks μ = 1 is always accepted immediately — the
    // converged Vm/Va must be bit-for-bit identical regardless of MaxStepHalvings.

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void Solve_VaryMaxStepHalvings_SameSolutionAsDisabled_Case14(int halvings)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var solver0 = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = 0 };
        var solverN = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = halvings };

        var r0 = solver0.Solve(net);
        var rN = solverN.Solve(net);

        Assert.True(r0.Converged);
        Assert.True(rN.Converged);

        for (int i = 0; i < net.Buses.Count; i++)
        {
            Assert.Equal(r0.Vm[i], rN.Vm[i], 10);
            Assert.Equal(r0.Va[i], rN.Va[i], 10);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Solve_VaryMaxStepHalvings_SameSolutionAsDisabled_Case300(int halvings)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case300.m"));
        var solver0 = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = 0 };
        var solverN = new NewtonRaphsonSolver { FlatStart = true, MaxStepHalvings = halvings };

        var r0 = solver0.Solve(net);
        var rN = solverN.Solve(net);

        Assert.True(r0.Converged);
        Assert.True(rN.Converged);

        for (int i = 0; i < net.Buses.Count; i++)
        {
            Assert.Equal(r0.Vm[i], rN.Vm[i], 10);
            Assert.Equal(r0.Va[i], rN.Va[i], 10);
        }
    }
}
