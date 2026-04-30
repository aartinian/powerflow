using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Solver;

/// <summary>
/// Validates the Newton-Raphson solver against published MATPOWER results for IEEE 14-bus.
/// Reference values taken from the MATPOWER case14 solution (runpf output).
/// </summary>
public class SolverValidationTests
{
    // MATPOWER reference solution for IEEE 14-bus (bus index 0-based).
    // Vm in pu, Va in degrees.
    private static readonly (double Vm, double Va)[] Case14Reference =
    [
        (1.0600, 0.0000), // bus 1  — slack
        (1.0450, -4.9826), // bus 2  — PV
        (1.0100, -12.7251), // bus 3  — PV
        (1.0185, -10.3128), // bus 4  — PQ
        (1.0199, -8.7739), // bus 5  — PQ
        (1.0700, -14.2209), // bus 6  — PV
        (1.0623, -13.3596), // bus 7  — PQ
        (1.0900, -13.3596), // bus 8  — PV
        (1.0559, -14.9385), // bus 9  — PQ
        (1.0510, -15.0973), // bus 10 — PQ
        (1.0569, -14.7906), // bus 11 — PQ
        (1.0552, -15.0759), // bus 12 — PQ
        (1.0504, -15.1564), // bus 13 — PQ
        (1.0355, -16.0337), // bus 14 — PQ
    ];

    [Fact]
    public void Solve_Case14_FlatStart_Converges()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(
            result.Converged,
            $"Did not converge — final mismatch: {result.MaxMismatch:e3} pu"
        );
        Assert.True(
            result.Iterations <= 10,
            $"Took {result.Iterations} iterations (expected ≤ 10)"
        );
    }

    [Fact]
    public void Solve_Case14_FlatStart_SlackBusPg()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        // MATPOWER reference: slack bus (bus 1) generates 232.4 MW = 2.324 pu
        Assert.Equal(2.324, result.Pg[0], 2); // ±0.005 pu
    }

    [Fact]
    public void Solve_Case14_FlatStart_PVBusQg()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        // MATPOWER runpf result: bus 2 Qg = 43.56 MVAr = 0.4356 pu
        // (The 42.4 MVAr stored in case14.m gen data is the initial dispatch, not the PF solution.)
        Assert.Equal(0.4356, result.Qg[1], 3); // ±0.0005 pu
    }

    [Fact]
    public void Solve_Case14_QLimitSwitch_Bus2AtCeiling()
    {
        // Gen 2 Qmax = 20 MVAr; natural Qg ≈ 42 MVAr → bus 2 must switch PV→PQ.
        var net = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged);
        Assert.Equal(0.200, result.Qg[1], 3); // Qg pinned at Qmax = 20 MVAr = 0.200 pu
        Assert.True(
            result.Vm[1] < 1.045, // voltage no longer held by PV constraint
            $"Expected bus 2 Vm < 1.045 after switch, got {result.Vm[1]:F4}"
        );
        // Vm < Vg (1.045) after switch at Qmax → recovery condition (Vm > Vg) not met → stays PQ.
        // This confirms the recovery guard does not fire when the limit is genuinely binding.
    }

    [Fact]
    public void Solve_Case14_QLimitSwitch_RecoveryDoesNotOscillate()
    {
        // Solve repeatedly with EnforceLimits — the outer loop must terminate within
        // MaxLimitIterations regardless of switch/recovery interplay.
        var net = MatpowerParser.ParseFile(TestData.Path("case14_qlimit.m"));
        var result = new NewtonRaphsonSolver { MaxLimitIterations = 10 }.Solve(net);

        Assert.True(result.Converged);
    }

    [Fact]
    public void Solve_Case14_FlatStart_BranchFlowCount()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(20, result.BranchFlows.Count); // all 20 case14 branches are in-service
    }

    [Fact]
    public void Solve_Case14_FlatStart_NoNegativeLosses()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        // Real losses P_ij + P_ji = R·|I|² ≥ 0 for every branch.
        foreach (var bf in result.BranchFlows)
            Assert.True(
                bf.Pij + bf.Pji >= -1e-6,
                $"Negative losses on branch {bf.FromBusId}→{bf.ToBusId}: {bf.Pij + bf.Pji:e3} pu"
            );
    }

    [Theory]
    [InlineData(0)] // slack
    [InlineData(1)] // PV
    [InlineData(3)] // PQ, behind transformer
    [InlineData(6)] // PQ, mid-network
    [InlineData(8)] // PQ, shunt bus
    [InlineData(13)] // PQ, tip bus
    public void Solve_Case14_FlatStart_VoltageMagnitude(int busIdx)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(Case14Reference[busIdx].Vm, result.Vm[busIdx], 3); // +/-0.0005 pu
    }

    [Theory]
    [InlineData(0)] // slack — must stay at 0°
    [InlineData(1)] // PV
    [InlineData(3)] // PQ, behind transformer
    [InlineData(6)] // PQ, mid-network
    [InlineData(8)] // PQ, shunt bus
    [InlineData(13)] // PQ, tip bus
    public void Solve_Case14_FlatStart_VoltageAngle(int busIdx)
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(Case14Reference[busIdx].Va, result.Va[busIdx], 2); // +/-0.005 degrees
    }

    // ─── Branch loading ─────────────────────────────────────────────────────────

    [Fact]
    public void Solve_Case14_FlatStart_LoadingPct_UnconstrainedIsNaN()
    {
        // case14_flatstart.m has rateA=0 for all branches — loading % is undefined.
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.All(result.BranchFlows, bf => Assert.True(double.IsNaN(bf.LoadingPct)));
    }

    [Fact]
    public void Solve_Case30_LoadingPct_FirstBranch()
    {
        // Branch 1→2 in case30: rateA=130 MVA, baseMVA=100.
        // From-end flow ≈ 10.89 MW, -5.09 MVAr → |S| ≈ 12.0 MVA → ~9.2 % loading.
        var net = MatpowerParser.ParseFile(TestData.Path("case30.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        var bf = result.BranchFlows[0]; // branch 1→2
        Assert.False(double.IsNaN(bf.LoadingPct));
        Assert.Equal(9.2, bf.LoadingPct, 1); // ±0.05 %
    }

    // ─── Voltage violations ──────────────────────────────────────────────────────

    [Fact]
    public void Solve_Case14_FlatStart_DetectsOvervoltageOnPVBuses()
    {
        // case14_flatstart.m has Vmax=1.06 for all buses.
        // Generator setpoints for buses 6 (Vg=1.07) and 8 (Vg=1.09) exceed that limit;
        // bus 7 (PQ, transformer-connected to bus 8) is pulled above 1.06 as well.
        var net = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(3, result.VoltageViolations.Count);
        Assert.All(result.VoltageViolations, v => Assert.True(v.IsOverVoltage));
        Assert.Contains(result.VoltageViolations, v => v.BusId == 6);
        Assert.Contains(result.VoltageViolations, v => v.BusId == 7);
        Assert.Contains(result.VoltageViolations, v => v.BusId == 8);
    }

    [Fact]
    public void Solve_Case30_NoVoltageViolations()
    {
        // case30.m uses Vmax=1.1 for PV buses; all generator setpoints are 1.0 pu — no violations.
        var net = MatpowerParser.ParseFile(TestData.Path("case30.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Empty(result.VoltageViolations);
    }

    // ─── FlatStart option ────────────────────────────────────────────────────

    [Fact]
    public void Solve_FlatStartOption_MatchesFlatStartCaseFile()
    {
        // case14.m has bus Vm/Va near the solution; case14_flatstart.m has the same
        // network with Vm=1, Va=0 baked in. Solving case14.m with FlatStart=true must
        // produce results bit-equivalent to solving case14_flatstart.m.
        var nearSolution = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var flatStartFile = MatpowerParser.ParseFile(TestData.Path("case14_flatstart.m"));

        var resultFromOption = new NewtonRaphsonSolver { FlatStart = true }.Solve(nearSolution);
        var resultFromFile   = new NewtonRaphsonSolver().Solve(flatStartFile);

        Assert.True(resultFromOption.Converged && resultFromFile.Converged);
        Assert.Equal(resultFromFile.Iterations, resultFromOption.Iterations);
        for (int i = 0; i < resultFromFile.Vm.Length; i++)
        {
            Assert.Equal(resultFromFile.Vm[i], resultFromOption.Vm[i], 12);
            Assert.Equal(resultFromFile.Va[i], resultFromOption.Va[i], 12);
            Assert.Equal(resultFromFile.Pg[i], resultFromOption.Pg[i], 12);
            Assert.Equal(resultFromFile.Qg[i], resultFromOption.Qg[i], 12);
        }
    }

    // ─── Out-of-service branches ─────────────────────────────────────────────

    [Fact]
    public void Solve_OutOfServiceBranch_BranchFlowsCountMatchesInServiceCount()
    {
        // case_test.m: 3 branches, branch 2→3 is out of service → expect 2 flow entries.
        var net = MatpowerParser.ParseFile(TestData.Path("case_test.m"));
        var inServiceCount = net.Branches.Count(b => b.IsInService);
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.Equal(inServiceCount, result.BranchFlows.Count);
    }

    [Fact]
    public void Solve_OutOfServiceBranch_OutOfServiceBranchNotInFlows()
    {
        // The out-of-service branch 2→3 must not appear in BranchFlows.
        var net = MatpowerParser.ParseFile(TestData.Path("case_test.m"));
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.DoesNotContain(result.BranchFlows, bf => bf.FromBusId == 2 && bf.ToBusId == 3);
    }

    // ─── Phase-shifting transformer ──────────────────────────────────────────

    /// <summary>
    /// Builds a minimal 2-bus network: slack → PQ load, connected by a single branch.
    /// </summary>
    private static PowerNetwork TwoBusNetwork(double phaseShiftDeg)
    {
        var slack = new Bus(1, BusType.Slack, 0, 0, 0, 0, 1.0, 0, 100, 1.1, 0.9);
        var load = new Bus(2, BusType.PQ, 100, 50, 0, 0, 1.0, 0, 100, 1.1, 0.9);
        var branch = new Branch(
            fromBus: 1,
            toBus: 2,
            r: 0.02,
            x: 0.10,
            b: 0,
            tapRatio: 1.0,
            phaseShift: phaseShiftDeg,
            rateA: 0,
            isInService: true
        );
        var gen = new Generator(1, 200, 0, 300, -300, 1.0, 500, 0, true);
        return new PowerNetwork(100, [slack, load], [branch], [gen]);
    }

    [Fact]
    public void Solve_PhaseShiftingTransformer_Converges()
    {
        var net = TwoBusNetwork(phaseShiftDeg: 10.0);
        var result = new NewtonRaphsonSolver().Solve(net);

        Assert.True(result.Converged, $"Did not converge — mismatch: {result.MaxMismatch:e3} pu");
    }

    [Fact]
    public void Solve_PhaseShiftingTransformer_AltersReactiveFlow()
    {
        // A non-zero phase shift must produce measurably different branch reactive flows
        // compared to the same network with zero phase shift.
        var base0 = new NewtonRaphsonSolver().Solve(TwoBusNetwork(phaseShiftDeg: 0));
        var phase5 = new NewtonRaphsonSolver().Solve(TwoBusNetwork(phaseShiftDeg: 5.0));

        Assert.True(base0.Converged);
        Assert.True(phase5.Converged);
        Assert.NotEqual(base0.BranchFlows[0].Qij, phase5.BranchFlows[0].Qij);
    }

    [Fact]
    public void Solve_PhaseShiftingTransformer_AltersRealFlow()
    {
        // Real power on the branch also changes with phase shift.
        var base0 = new NewtonRaphsonSolver().Solve(TwoBusNetwork(phaseShiftDeg: 0));
        var phase5 = new NewtonRaphsonSolver().Solve(TwoBusNetwork(phaseShiftDeg: 5.0));

        Assert.True(base0.Converged);
        Assert.True(phase5.Converged);
        Assert.NotEqual(base0.BranchFlows[0].Pij, phase5.BranchFlows[0].Pij);
    }
}
