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
}
