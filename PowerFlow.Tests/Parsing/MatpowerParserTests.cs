using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

namespace PowerFlow.Tests.Parsing;

public class MatpowerParserTests
{
    private static PowerNetwork ParseTestCase() =>
        MatpowerParser.ParseFile(TestData.Path("case_test.m"));

    [Fact]
    public void Parse_BaseMva()
    {
        Assert.Equal(100.0, ParseTestCase().BaseMva);
    }

    [Fact]
    public void Parse_ElementCounts()
    {
        var net = ParseTestCase();
        Assert.Equal(3, net.Buses.Count);
        Assert.Equal(2, net.Generators.Count);
        Assert.Equal(3, net.Branches.Count);
    }

    [Fact]
    public void Parse_BusTypes()
    {
        var net = ParseTestCase();
        Assert.Equal(BusType.Slack, net.Buses[0].Type);
        Assert.Equal(BusType.PV, net.Buses[1].Type);
        Assert.Equal(BusType.PQ, net.Buses[2].Type);
    }

    [Fact]
    public void Parse_BusFields()
    {
        var bus3 = ParseTestCase().Buses[2];
        Assert.Equal(3, bus3.Id);
        Assert.Equal(94.2, bus3.Pd);
        Assert.Equal(19.0, bus3.Qd);
        Assert.Equal(1.010, bus3.Vm);
        Assert.Equal(-12.72, bus3.Va);
        Assert.Equal(132.0, bus3.BaseKv);
    }

    [Fact]
    public void Parse_GeneratorFields()
    {
        var gen2 = ParseTestCase().Generators[1];
        Assert.Equal(2, gen2.BusId);
        Assert.Equal(40.0, gen2.Pg);
        Assert.Equal(50.0, gen2.Qmax);
        Assert.Equal(-40.0, gen2.Qmin);
        Assert.Equal(1.045, gen2.Vg);
        Assert.True(gen2.IsInService);
    }

    [Fact]
    public void Parse_BranchFields()
    {
        var branch = ParseTestCase().Branches[0];
        Assert.Equal(1, branch.FromBus);
        Assert.Equal(2, branch.ToBus);
        Assert.Equal(0.01938, branch.R);
        Assert.Equal(0.05917, branch.X);
        Assert.Equal(0.0528, branch.B);
    }

    [Fact]
    public void Parse_TapRatioZeroNormalisedToOne()
    {
        // All three branches have ratio=0 in the case file (plain lines).
        Assert.All(ParseTestCase().Branches, b => Assert.Equal(1.0, b.TapRatio));
    }

    [Fact]
    public void Parse_OutOfServiceBranch()
    {
        // Branch 2-3 has status=0 in the case file.
        Assert.False(ParseTestCase().Branches[2].IsInService);
    }

    [Fact]
    public void Parse_GencostBlockDoesNotCorruptGenerators()
    {
        // Verifies that mpc.gencost is not accidentally parsed as mpc.gen.
        Assert.Equal(2, ParseTestCase().Generators.Count);
    }

    [Fact]
    public void Parse_Case14_ElementCounts()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        Assert.Equal(14, net.Buses.Count);
        Assert.Equal(5, net.Generators.Count);
        Assert.Equal(20, net.Branches.Count);
    }

    [Fact]
    public void Parse_Case14_TransformerTapRatios()
    {
        // Branches 4-7, 4-9, 5-6 are transformers with non-unity tap ratios.
        var branches = MatpowerParser.ParseFile(TestData.Path("case14.m")).Branches;
        Assert.Equal(0.978, branches[7].TapRatio);
        Assert.Equal(0.969, branches[8].TapRatio);
        Assert.Equal(0.932, branches[9].TapRatio);
    }

    // ── RateB / RateC ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BranchRateB_RateC_WhenZero()
    {
        // case_test.m and case14.m have rateB=rateC=0 for all branches.
        Assert.All(
            ParseTestCase().Branches,
            b =>
            {
                Assert.Equal(0.0, b.RateB);
                Assert.Equal(0.0, b.RateC);
            }
        );
    }

    [Fact]
    public void Parse_Case30_BranchRateB_RateC_NonZero()
    {
        // case30.m has identical rateA=rateB=rateC=130 for the first branch (bus 1→2).
        var branch = MatpowerParser.ParseFile(TestData.Path("case30.m")).Branches[0];
        Assert.Equal(130.0, branch.RateA);
        Assert.Equal(130.0, branch.RateB);
        Assert.Equal(130.0, branch.RateC);
    }

    [Fact]
    public void Parse_Case30_AllBranches_RateBEqualsRateC()
    {
        // In case30.m every branch has rateA=rateB=rateC (standard format).
        var branches = MatpowerParser.ParseFile(TestData.Path("case30.m")).Branches;
        Assert.All(branches, b => Assert.Equal(b.RateA, b.RateC));
        Assert.All(branches, b => Assert.Equal(b.RateA, b.RateB));
    }

    // ── mpc.shunt block ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ShuntBlock_FoldsIntoMatchingBusAdmittance()
    {
        // case_shunt.m: bus 2 has Bs=0 in mpc.bus; mpc.shunt adds Bs=10 → result Bs=10.
        var net = MatpowerParser.ParseFile(TestData.Path("case_shunt.m"));

        Assert.Equal(10.0, net.Buses[1].Bs);
    }

    [Fact]
    public void Parse_ShuntBlock_GsZeroWhenShuntHasZeroGs()
    {
        // The mpc.shunt entry has Gs=0, so net Gs on bus 2 must stay 0.
        var net = MatpowerParser.ParseFile(TestData.Path("case_shunt.m"));

        Assert.Equal(0.0, net.Buses[1].Gs);
    }

    [Fact]
    public void Parse_ShuntBlock_OtherBusesUnaffected()
    {
        // Bus 1 (slack) has no shunt entry — its Gs and Bs must stay 0.
        var net = MatpowerParser.ParseFile(TestData.Path("case_shunt.m"));

        Assert.Equal(0.0, net.Buses[0].Gs);
        Assert.Equal(0.0, net.Buses[0].Bs);
    }

    [Fact]
    public void Parse_ShuntBlock_SolveMatchesEquivalentDirectBusData()
    {
        // The parsed case_shunt.m (shunt via mpc.shunt) must produce the same
        // power-flow solution as an equivalent network with Bs=10 in the bus data.
        var netFromFile = MatpowerParser.ParseFile(TestData.Path("case_shunt.m"));

        // Equivalent network: same topology but Bs=10 declared directly on bus 2.
        var slack = new Bus(1, BusType.Slack, 0, 0, 0, 0, 1.0, 0, 100, 1.1, 0.9);
        var load = new Bus(2, BusType.PQ, 100, 50, 0, 10, 1.0, 0, 100, 1.1, 0.9);
        var branch = new Branch(1, 2, 0.02, 0.10, 0, 1.0, 0, 200, true);
        var gen = new Generator(1, 200, 0, 300, -300, 1.0, 400, 0, true);
        var netRef = new PowerNetwork(100, [slack, load], [branch], [gen]);

        var solver = new NewtonRaphsonSolver();
        var rFile = solver.Solve(netFromFile);
        var rRef = solver.Solve(netRef);

        Assert.True(rFile.Converged);
        Assert.True(rRef.Converged);
        Assert.Equal(rRef.Vm[0], rFile.Vm[0], 8);
        Assert.Equal(rRef.Vm[1], rFile.Vm[1], 8);
        Assert.Equal(rRef.Va[1], rFile.Va[1], 8);
    }

    [Fact]
    public void Parse_NoShuntBlock_ParsesWithoutError()
    {
        // Cases without mpc.shunt (the majority) must parse normally.
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));

        Assert.Equal(14, net.Buses.Count);
    }
}
