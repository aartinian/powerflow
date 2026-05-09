using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Tests for the linearised (DC) power-flow solver.
/// DC assumptions: V = 1 pu everywhere, R = 0, shunts ignored.
/// The solve is a single sparse-LU step; the only "convergence" question
/// is whether the matrix is non-singular (caught earlier by NetworkValidator).
/// </summary>
public class DcPowerFlowSolverTests
{
    // ── Structural correctness ────────────────────────────────────────────

    [Fact]
    public void Solve_Case14_SlackBusAngleIsZero()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new DcPowerFlowSolver().Solve(net);

        int slackIdx = net.Buses.ToList().FindIndex(b => b.Type == BusType.Slack);
        Assert.Equal(0.0, result.Va[slackIdx]);
    }

    [Fact]
    public void Solve_Case14_AllAnglesFinite()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new DcPowerFlowSolver().Solve(net);

        Assert.All(result.Va, a => Assert.True(double.IsFinite(a), $"Non-finite angle: {a}"));
    }

    [Fact]
    public void Solve_Case14_BranchFlowCountMatchesInServiceBranches()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new DcPowerFlowSolver().Solve(net);

        int expected = net.Branches.Count(b => b.IsInService);
        Assert.Equal(expected, result.BranchFlows.Count);
    }

    // ── Power balance (DC has zero losses) ───────────────────────────────

    /// <summary>
    /// For a lossless DC network Σ P_injected = 0, so total generation
    /// must exactly equal total load.
    /// </summary>
    [Theory]
    [InlineData("case14.m")]
    [InlineData("case118.m")]
    [InlineData("case300.m")]
    public void Solve_TotalGenerationEqualsTotalLoad(string caseFile)
    {
        var net = MatpowerParser.ParseFile(TestData.Path(caseFile));
        var result = new DcPowerFlowSolver().Solve(net);

        double totalPg = result.Pg.Sum() * net.BaseMva;
        double totalPd = net.Buses.Sum(b => b.Pd);

        Assert.Equal(
            totalPd,
            totalPg,
            3 /* decimal places → ±0.0005 MW */
        );
    }

    // ── Large-case smoke tests ────────────────────────────────────────────

    [Fact]
    public void Solve_Case118_AllAnglesFinite()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case118.m"));
        var result = new DcPowerFlowSolver().Solve(net);

        Assert.All(result.Va, a => Assert.True(double.IsFinite(a), $"Non-finite angle: {a}"));
    }

    [Fact]
    public void Solve_Case300_AllAnglesFinite()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case300.m"));
        var result = new DcPowerFlowSolver().Solve(net);

        Assert.All(result.Va, a => Assert.True(double.IsFinite(a), $"Non-finite angle: {a}"));
    }

    // ── Accuracy vs AC solution ───────────────────────────────────────────

    /// <summary>
    /// For a well-conditioned case (small R/X, lightly loaded), DC angles
    /// should be within a few degrees of the AC solution.
    /// </summary>
    [Fact]
    public void Solve_Case14_AnglesCloseToACSolution()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var ac = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var dc = new DcPowerFlowSolver().Solve(net);

        Assert.True(ac.Converged);
        for (int i = 0; i < net.Buses.Count; i++)
        {
            double diff = Math.Abs(dc.Va[i] - ac.Va[i]);
            Assert.True(
                diff < 2.0,
                $"Bus {net.Buses[i].Id}: DC={dc.Va[i]:F3}°  AC={ac.Va[i]:F3}°  |diff|={diff:F3}°"
            );
        }
    }

    // ── Edge case: single-bus (slack-only) network ────────────────────────

    [Fact]
    public void Solve_SingleBus_ReturnsZeroAngleAndEmptyFlows()
    {
        // Bus(id, type, pd, qd, gs, bs, vm, va, basekv, vmax, vmin)
        var slack = new Bus(1, BusType.Slack, 0, 0, 0, 0, 1.0, 0, 100, 1.1, 0.9);
        // Generator(busId, pg, qg, pmax, qmax, vg, pmin, qmin, isInService)
        var gen = new Generator(1, 100, 0, 200, -100, 1.0, 0, -100, true);
        var net = new PowerNetwork(100, [slack], [], [gen]);

        var result = new DcPowerFlowSolver().Solve(net);

        Assert.Single(result.Va);
        Assert.Equal(0.0, result.Va[0]);
        Assert.Empty(result.BranchFlows);
    }
}
