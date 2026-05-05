using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Tests for v0.8.0 result-completeness additions:
/// SystemBalance, per-generator results, kV reporting, and Q-limit binding info.
/// </summary>
public class SolverResultCompletenessTests
{
    // ─── SystemBalance ──────────────────────────────────────────────────────────

    [Fact]
    public void SystemBalance_NotNull_WhenConverged()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.NotNull(result.Balance);
    }

    [Fact]
    public void SystemBalance_IsNull_WhenNotConverged()
    {
        // Force non-convergence by allowing only 1 iteration on a non-trivial network.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, MaxIterations = 1 }.Solve(net);

        Assert.False(result.Converged);
        Assert.Null(result.Balance);
    }

    [Fact]
    public void SystemBalance_TotalLossesMw_MatchesBranchFlowSum()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        double expected = result.BranchFlows.Sum(bf => (bf.Pij + bf.Pji) * net.BaseMva);
        Assert.Equal(expected, result.Balance!.TotalLossesMw, precision: 4);
    }

    [Fact]
    public void SystemBalance_PowerBalance_Consistent()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var b      = result.Balance!;

        // Generation ≈ Load + Losses (within solver tolerance scaled to MW).
        Assert.Equal(b.TotalGenerationMw, b.TotalLoadMw + b.TotalLossesMw, precision: 2);
    }

    [Fact]
    public void SystemBalance_LossPct_ConsistentWithMwValues()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var b      = result.Balance!;

        double expected = b.TotalLoadMw > 0 ? b.TotalLossesMw / b.TotalLoadMw * 100.0 : 0.0;
        Assert.Equal(expected, b.LossPct, precision: 6);
    }

    // ─── Per-generator results ──────────────────────────────────────────────────

    [Fact]
    public void Generators_Count_EqualsInServiceGeneratorCount()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        int expected = net.Generators.Count(g => g.IsInService);
        Assert.Equal(expected, result.Generators.Count);
    }

    [Fact]
    public void Generators_BusIds_MatchNetworkGenerators()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        var expectedIds = net.Generators
            .Where(g => g.IsInService)
            .Select(g => g.BusId)
            .OrderBy(id => id)
            .ToList();
        var actualIds = result.Generators
            .Select(gr => gr.BusId)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(expectedIds, actualIds);
    }

    [Fact]
    public void Generators_Pg_NonNegative_ForAllGenerators()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case118.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.All(result.Generators, gr => Assert.True(gr.Pg >= -1e-3,
            $"Generator at bus {gr.BusId} has negative Pg = {gr.Pg:F3} MW"));
    }

    [Fact]
    public void Generators_PvBus_PgMatchesSolverBusPg()
    {
        // For PV buses the scheduled dispatch is held fixed by the solver, so
        // GeneratorResult.Pg (= gen.Pg from input) should match the solved bus-level
        // Pg within numerical tolerance. The slack bus is excluded — its output floats.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        var pvBusIndices = net.Buses
            .Select((b, i) => (b, i))
            .Where(t => t.b.Type == PowerFlow.Core.Models.BusType.PV)
            .ToDictionary(t => t.b.Id, t => t.i);

        foreach (var gr in result.Generators.Where(g => pvBusIndices.ContainsKey(g.BusId)))
        {
            int idx       = pvBusIndices[gr.BusId];
            double busPg  = result.Pg[idx] * net.BaseMva;
            Assert.Equal(gr.Pg, busPg, precision: 1); // within 0.05 MW
        }
    }

    [Fact]
    public void Generators_DistributedSlack_PgIncludesLambdaCorrection()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver
        {
            FlatStart        = true,
            DistributedSlack = true,
        }.Solve(net);

        Assert.True(result.Converged);
        // With distributed slack λ ≠ 0, total gen-result Pg should still match bus-level sum.
        double busTotal = result.Pg.Sum() * net.BaseMva;
        double genTotal = result.Generators.Sum(gr => gr.Pg);
        Assert.Equal(busTotal, genTotal, precision: 2);
    }

    // ─── kV reporting on VoltageViolation ──────────────────────────────────────

    [Fact]
    public void VoltageViolation_VmKv_EqualsPuTimesBaseKv()
    {
        // case14 bus 8 (a shunt compensator at 1.09 pu) exceeds Vmax, giving a violation.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.NotEmpty(result.VoltageViolations);

        foreach (var v in result.VoltageViolations)
        {
            // Find bus BaseKv from the network.
            var bus = net.Buses.First(b => b.Id == v.BusId);
            Assert.Equal(v.Vm   * bus.BaseKv, v.VmKv,   precision: 9);
            Assert.Equal(v.Vmin * bus.BaseKv, v.VminKv, precision: 9);
            Assert.Equal(v.Vmax * bus.BaseKv, v.VmaxKv, precision: 9);
        }
    }

    [Fact]
    public void VoltageViolation_BaseKv_MatchesNetworkBusData()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.NotEmpty(result.VoltageViolations);

        var busById = net.Buses.ToDictionary(b => b.Id);
        Assert.All(result.VoltageViolations, v =>
            Assert.Equal(busById[v.BusId].BaseKv, v.BaseKv));
    }

    // ─── Q-limit binding info ───────────────────────────────────────────────────

    [Fact]
    public void QLimitBound_Empty_WhenLimitsDisabled()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true, EnforceLimits = false }.Solve(net);

        Assert.Empty(result.QLimitBound);
    }

    [Fact]
    public void QLimitBound_ContainsBus2_WhenQmaxTightened()
    {
        // case14_qlimit has gen at bus 2 with Qmax=20 MVAr; natural Qg ~42 MVAr → ceiling.
        var net    = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        Assert.True(result.QLimitBound.ContainsKey(2),
            "Bus 2 should be pinned at Qmax with tightened Qmax=20 MVAr");
        Assert.True(result.QLimitBound[2],
            "Bus 2 should be at Qmax (not Qmin)");
    }

    [Fact]
    public void QLimitBound_Keys_AreValidBusIds()
    {
        var net      = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result   = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);
        var validIds = net.Buses.Select(b => b.Id).ToHashSet();

        Assert.All(result.QLimitBound.Keys, id =>
            Assert.Contains(id, validIds));
    }

    [Fact]
    public void QLimitBound_BoundGenerators_HaveIsAtQmaxOrIsAtQmin_Set()
    {
        var net    = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver { FlatStart = true }.Solve(net);

        Assert.True(result.Converged);
        foreach (var (busId, atMax) in result.QLimitBound)
        {
            var gens = result.Generators.Where(gr => gr.BusId == busId).ToList();
            Assert.NotEmpty(gens);
            Assert.All(gens, gr =>
            {
                Assert.Equal(atMax, gr.IsAtQmax);
                Assert.Equal(!atMax, gr.IsAtQmin);
            });
        }
    }
}
