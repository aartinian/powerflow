using System.Numerics;
using PowerFlow.Core.Models;
using PowerFlow.Core.Network;

namespace PowerFlow.Tests.Network;

public class YBusBuilderTests
{
    private const double Tol = 1e-10;

    private static void AssertComplex(Complex expected, Complex actual)
    {
        Assert.True(
            Math.Abs(expected.Real - actual.Real) < Tol,
            $"Real part: expected {expected.Real} but got {actual.Real}"
        );
        Assert.True(
            Math.Abs(expected.Imaginary - actual.Imaginary) < Tol,
            $"Imaginary part: expected {expected.Imaginary} but got {actual.Imaginary}"
        );
    }

    private static Bus MakeBus(int id, BusType type = BusType.PQ, double gs = 0, double bs = 0) =>
        new(id, type, 0, 0, gs, bs, 1.0, 0.0, 1.0, 1.1, 0.9);

    private static Branch MakeLine(int from, int to, double r, double x, double b) =>
        new(from, to, r, x, b, tapRatio: 0, phaseShift: 0, isInService: true);

    private static Branch MakeTransformer(int from, int to, double x, double tap) =>
        new(from, to, 0, x, 0, tapRatio: tap, phaseShift: 0, isInService: true);

    // 2-bus, one transmission line: R=0.1 X=0.2 B=0.04
    // ys = 1/(0.1+j0.2) = 2-j4   yc = j0.02
    private static PowerNetwork TwoBusLine()
    {
        var buses = new[] { MakeBus(1, BusType.Slack), MakeBus(2) };
        var branches = new[] { MakeLine(1, 2, r: 0.1, x: 0.2, b: 0.04) };
        return new PowerNetwork(100, buses, branches, []);
    }

    [Fact]
    public void Build_TwoBusLine_DiagonalEntries()
    {
        var Y = YBusBuilder.Build(TwoBusLine());
        AssertComplex(new Complex(2.0, -3.98), Y[0, 0]);
        AssertComplex(new Complex(2.0, -3.98), Y[1, 1]);
    }

    [Fact]
    public void Build_TwoBusLine_OffDiagonalEntries()
    {
        var Y = YBusBuilder.Build(TwoBusLine());
        AssertComplex(new Complex(-2.0, 4.0), Y[0, 1]);
        AssertComplex(new Complex(-2.0, 4.0), Y[1, 0]);
    }

    [Fact]
    public void Build_TwoBusLine_IsSymmetric()
    {
        var Y = YBusBuilder.Build(TwoBusLine());
        AssertComplex(Y[0, 1], Y[1, 0]);
    }

    // 2-bus transformer: X=0.2 tap=0.978
    // ys = -j5   Y[0,0] = -j5/0.978²   Y[1,1] = -j5   off-diag = j5/0.978
    [Fact]
    public void Build_Transformer_AsymmetricDiagonal()
    {
        var net = new PowerNetwork(
            100,
            new[] { MakeBus(1, BusType.Slack), MakeBus(2) },
            new[] { MakeTransformer(1, 2, x: 0.2, tap: 0.978) },
            []
        );
        var Y = YBusBuilder.Build(net);

        double tMagSq = 0.978 * 0.978;
        AssertComplex(new Complex(0, -5.0 / tMagSq), Y[0, 0]); // from-bus: divided by |t|²
        AssertComplex(new Complex(0, -5.0), Y[1, 1]); // to-bus: unchanged
    }

    [Fact]
    public void Build_Transformer_OffDiagonalSymmetricWithoutPhaseShift()
    {
        var net = new PowerNetwork(
            100,
            new[] { MakeBus(1, BusType.Slack), MakeBus(2) },
            new[] { MakeTransformer(1, 2, x: 0.2, tap: 0.978) },
            []
        );
        var Y = YBusBuilder.Build(net);

        // No phase shift → t is real → t* = t → Y[0,1] == Y[1,0]
        AssertComplex(Y[0, 1], Y[1, 0]);
        AssertComplex(new Complex(0, 5.0 / 0.978), Y[0, 1]);
    }

    [Fact]
    public void Build_BusShunt_AddedToDiagonal()
    {
        var bus = new Bus(
            1,
            BusType.Slack,
            pd: 0,
            qd: 0,
            gs: 50,
            bs: 10,
            vm: 1.0,
            va: 0,
            baseKv: 1.0,
            vmax: 1.1,
            vmin: 0.9
        );
        var net = new PowerNetwork(100, new[] { bus }, [], []);
        var Y = YBusBuilder.Build(net);

        // Gs=50 MW, Bs=10 MVAr → pu: 50/100 + j10/100 = 0.5 + j0.1
        AssertComplex(new Complex(0.5, 0.1), Y[0, 0]);
    }

    [Fact]
    public void Build_OutOfServiceBranch_IsIgnored()
    {
        var buses = new[] { MakeBus(1, BusType.Slack), MakeBus(2) };
        var branch = new Branch(1, 2, 0.1, 0.2, 0.04, 0, 0, isInService: false);
        var Y = YBusBuilder.Build(new PowerNetwork(100, buses, new[] { branch }, []));

        AssertComplex(Complex.Zero, Y[0, 0]);
        AssertComplex(Complex.Zero, Y[0, 1]);
    }
}
