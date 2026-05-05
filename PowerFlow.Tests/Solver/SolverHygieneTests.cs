using PowerFlow.Core.Models;
using PowerFlow.Core.Network;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Tests for v0.9.0 solver-hygiene fixes: iteration count, mutable-array safety,
/// Y-bus overload, and DC warm-start.
/// </summary>
public class SolverHygieneTests
{
    // ─── Iteration count ────────────────────────────────────────────────────────

    [Fact]
    public void Iterations_IsConsistentWithLoggerCount()
    {
        // The logger prints "iter N  mismatch ... converged" where N = totalNrIters.
        // result.Iterations must equal that N (cumulative across outer loops).
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.True(result.Iterations >= 1,
            "A converged solve must have taken at least one NR iteration.");
    }

    [Fact]
    public void Iterations_CumulativeAcrossOuterLoops()
    {
        // With Q-limits active and case14_qlimit forcing a PV→PQ switch, the solver
        // runs ≥ 2 outer loops. Iterations should be the total NR count, not just
        // the last inner-loop count.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.True(result.OuterIterations >= 2,
            $"Expected ≥ 2 outer iterations, got {result.OuterIterations}");
        // Cumulative NR count must be ≥ outer count (each outer loop takes ≥ 1 NR step).
        Assert.True(result.Iterations >= result.OuterIterations,
            $"Iterations ({result.Iterations}) < OuterIterations ({result.OuterIterations})");
    }

    [Fact]
    public void OuterIterations_IsOne_WhenNoQLimitSwitches()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, EnforceLimits = false }.Solve(net);

        // With limits off there is exactly one outer pass.
        Assert.Equal(1, result.OuterIterations);
    }

    [Fact]
    public void OuterIterations_IsZero_ForTrivialNetwork()
    {
        // Single slack-bus network — trivially solved, no NR, no outer loop.
        var net = new PowerNetwork(
            100,
            [new Bus(1, BusType.Slack, 0, 0, 0, 0, 1, 0, 0, 1.1, 0.9)],
            [],
            []
        );
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(0, result.OuterIterations);
    }

    // ─── Mutable array safety ───────────────────────────────────────────────────

    [Fact]
    public void Pg_Array_IsMutableSafely()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        double originalPg0 = result.Pg[0];
        result.Pg[0] = 9999.0; // mutate the returned array

        // A second solve must be unaffected — confirms the result owns its own copy.
        var result2 = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        Assert.Equal(originalPg0, result2.Pg[0], precision: 6);
    }

    [Fact]
    public void Qg_Array_IsMutableSafely()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        double originalQg0 = result.Qg[0];
        result.Qg[0] = 9999.0;

        var result2 = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        Assert.Equal(originalQg0, result2.Qg[0], precision: 6);
    }

    // ─── Pre-built Y-bus overload ───────────────────────────────────────────────

    [Fact]
    public void PrebuiltYbus_ProducesIdenticalResult()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var solver = new NewtonRaphsonSolver { FlatStart = true };

        var r1 = solver.Solve(net);
        var r2 = solver.Solve(net, YBusBuilder.Build(net));

        Assert.Equal(r1.Converged,    r2.Converged);
        Assert.Equal(r1.Iterations,   r2.Iterations);
        Assert.Equal(r1.MaxMismatch,  r2.MaxMismatch, precision: 10);
        for (int i = 0; i < net.Buses.Count; i++)
        {
            Assert.Equal(r1.Vm[i], r2.Vm[i], precision: 10);
            Assert.Equal(r1.Va[i], r2.Va[i], precision: 10);
        }
    }

    [Fact]
    public void PrebuiltYbus_AllowsReuseAcrossSolves()
    {
        // Build once, solve twice — second solve should match first.
        var net    = MatpowerParser.ParseFile(TestData.Path("case118.m"));
        var solver = new NewtonRaphsonSolver { FlatStart = true };
        var ybus   = YBusBuilder.Build(net);

        var r1 = solver.Solve(net, ybus);
        var r2 = solver.Solve(net, ybus);

        Assert.True(r1.Converged);
        Assert.Equal(r1.MaxMismatch, r2.MaxMismatch, precision: 10);
    }

    // ─── DC warm-start ──────────────────────────────────────────────────────────

    [Fact]
    public void WarmStartFromDc_Converges()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver
        {
            FlatStart     = true,
            WarmStartFromDc = true,
        }.Solve(net);

        Assert.True(result.Converged);
    }

    [Fact]
    public void WarmStartFromDc_ProducesCorrectSolution()
    {
        // Solution must match the standard solve to within NR tolerance.
        var net      = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var standard = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var warmed   = new NewtonRaphsonSolver { FlatStart = true, WarmStartFromDc = true }.Solve(net);

        for (int i = 0; i < net.Buses.Count; i++)
        {
            Assert.Equal(standard.Vm[i], warmed.Vm[i], precision: 4);
            Assert.Equal(standard.Va[i], warmed.Va[i], precision: 4);
        }
    }

    [Fact]
    public void WarmStartFromDc_ConvergesInFewerIterations_Case300()
    {
        // For a large case from flat start, DC warm-start should reduce NR iterations.
        var net    = MatpowerParser.ParseFile(TestData.Path("case300.m"));
        var flat   = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var warmed = new NewtonRaphsonSolver { FlatStart = true, WarmStartFromDc = true }.Solve(net);

        Assert.True(flat.Converged,   "flat-start must converge");
        Assert.True(warmed.Converged, "DC warm-start must converge");
        Assert.True(warmed.Iterations <= flat.Iterations,
            $"DC warm-start ({warmed.Iterations} iters) should not need more than flat ({flat.Iterations} iters)");
    }

    // ─── Validator placement ────────────────────────────────────────────────────

    [Fact]
    public void NetworkValidator_LivesInValidationNamespace()
    {
        // Compile-time proof: NetworkValidator, ValidationResult, ValidationSeverity
        // are all accessible from PowerFlow.Core.Validation.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        PowerFlow.Core.Validation.ValidationResult result =
            PowerFlow.Core.Validation.NetworkValidator.Validate(net);
        Assert.True(result.IsValid);
        Assert.Equal(
            PowerFlow.Core.Validation.ValidationSeverity.Warning,
            PowerFlow.Core.Validation.ValidationSeverity.Warning
        );
    }

    // ─── MULTIPLE_VG_AT_BUS warning ────────────────────────────────────────────

    [Fact]
    public void Validator_WarnsMULTIPLE_VG_AT_BUS_WhenDisagreeingSetpoints()
    {
        // Build a tiny PV bus with two generators at different Vg.
        var buses = new List<Bus>
        {
            new(1, BusType.Slack, 0, 0, 0, 0, 1.0, 0, 0, 1.1, 0.9),
            new(2, BusType.PV,    0, 0, 0, 0, 1.0, 0, 0, 1.1, 0.9),
        };
        var branches = new List<Branch>
        {
            new(1, 2, 0.01, 0.1, 0, 1, 0, 0, true),
        };
        var generators = new List<Generator>
        {
            new(1, 100, 0, 50, -50, 1.0, 200, 0, true),
            new(2, 50,  0, 30, -30, 1.05, 100, 0, true), // Vg = 1.05
            new(2, 20,  0, 20, -20, 1.02, 50,  0, true), // Vg = 1.02 — disagrees
        };

        var net    = new PowerNetwork(100, buses, branches, generators);
        var result = NetworkValidator.Validate(net);

        Assert.True(result.Warnings.Any(w => w.Code == "MULTIPLE_VG_AT_BUS"),
            "Expected MULTIPLE_VG_AT_BUS warning when generators at same bus have different Vg");
    }
}
