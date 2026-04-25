using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;

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
}
