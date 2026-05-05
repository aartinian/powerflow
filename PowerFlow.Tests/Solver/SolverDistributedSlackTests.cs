using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Tests for the distributed-slack augmented Newton-Raphson formulation.
/// The augmented system adds one scalar variable λ and one equation (reference-bus P)
/// so that the generation imbalance is shared across all generators in proportion
/// to their Pmax, rather than being absorbed solely by the slack bus.
/// </summary>
public class SolverDistributedSlackTests
{
    // ── Convergence ──────────────────────────────────────────────────────────

    [Fact]
    public void Solve_DistributedSlack_Case14Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(
            net
        );

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    [Fact]
    public void Solve_DistributedSlack_Case118Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case118.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(
            net
        );

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    [Fact]
    public void Solve_DistributedSlack_Case300Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case300.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(
            net
        );

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
    }

    // ── Lambda semantics ─────────────────────────────────────────────────────

    [Fact]
    public void Solve_StandardNR_LambdaIsAlwaysZero()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = false }.Solve(
            net
        );

        Assert.True(result.Converged);
        Assert.Equal(0.0, result.Lambda);
    }

    [Fact]
    public void Solve_DistributedSlack_LambdaIsFinite()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(
            net
        );

        Assert.True(result.Converged);
        Assert.True(double.IsFinite(result.Lambda), $"Lambda is not finite: {result.Lambda}");
    }

    /// <summary>
    /// λ represents the total generation imbalance redistributed across participating buses.
    /// For standard MATPOWER test cases this equals the power the slack bus would otherwise
    /// absorb unilaterally, which can be tens of MW for small cases and hundreds of MW for
    /// large ones. We only verify that λ is finite and the solver converged.
    /// </summary>
    [Theory]
    [InlineData("case14.m")]
    [InlineData("case118.m")]
    [InlineData("case300.m")]
    public void Solve_DistributedSlack_LambdaIsFiniteAfterConvergence(string caseFile)
    {
        var net = MatpowerParser.ParseFile(TestData.Path(caseFile));
        var result = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(
            net
        );

        Assert.True(result.Converged, $"Did not converge — {result.MaxMismatch:e3} pu");
        Assert.True(double.IsFinite(result.Lambda), $"Lambda is not finite: {result.Lambda}");
    }

    // ── Consistency ──────────────────────────────────────────────────────────

    /// <summary>
    /// Standard and distributed-slack solves must converge to the same total system
    /// loading (ΣPd is a network constant, unaffected by how imbalance is shared).
    /// </summary>
    [Fact]
    public void Solve_DistributedSlack_TotalLoadUnchanged_Case14()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var rStd = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = false }.Solve(
            net
        );
        var rDs = new NewtonRaphsonSolver { FlatStart = true, DistributedSlack = true }.Solve(net);

        Assert.True(rStd.Converged);
        Assert.True(rDs.Converged);

        double pdTotal = net.Buses.Sum(b => b.Pd);
        double pgStd = rStd.Pg.Sum() * net.BaseMva;
        double pgDs = rDs.Pg.Sum() * net.BaseMva;

        // Both formulations must supply the load + losses; the two totals should agree
        // to within a loose tolerance since they solve the same physical network.
        Assert.Equal(
            pgStd,
            pgDs,
            1 /* decimal places → ±0.05 MW */
        );
    }
}
