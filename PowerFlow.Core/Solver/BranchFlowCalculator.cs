using System.Numerics;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Solver;

/// <summary>
/// Computes complex power flow at each end of every in-service branch from the
/// solved bus voltages. The calculation re-derives the same off-nominal-tap π
/// model used by <see cref="Network.YBusBuilder"/>, but evaluates the branch
/// currents instead of contributing to the admittance matrix.
/// </summary>
public static class BranchFlowCalculator
{
    /// <summary>
    /// Returns one <see cref="BranchFlow"/> per in-service branch.
    /// Out-of-service branches are skipped.
    /// </summary>
    public static IReadOnlyList<BranchFlow> Compute(PowerNetwork network, double[] Vm, double[] Va)
    {
        var flows = new List<BranchFlow>(network.Branches.Count);

        foreach (var br in network.Branches)
        {
            if (!br.IsInService)
                continue;

            int i = network.IndexOf(br.FromBus);
            int j = network.IndexOf(br.ToBus);

            var ys = Complex.One / new Complex(br.R, br.X);
            var yc = new Complex(0.0, br.B / 2.0);

            double phi = br.PhaseShift * Math.PI / 180.0;
            var t = new Complex(br.TapRatio * Math.Cos(phi), br.TapRatio * Math.Sin(phi));
            double tMagSq = br.TapRatio * br.TapRatio;

            var Vi = Complex.FromPolarCoordinates(Vm[i], Va[i]);
            var Vj = Complex.FromPolarCoordinates(Vm[j], Va[j]);

            // Same π model as YBusBuilder.
            var Iij = ((ys + yc) / tMagSq) * Vi - (ys / Complex.Conjugate(t)) * Vj;
            var Iji = -(ys / t) * Vi + (ys + yc) * Vj;

            var Sij = Vi * Complex.Conjugate(Iij);
            var Sji = Vj * Complex.Conjugate(Iji);

            flows.Add(
                new BranchFlow(
                    br.FromBus,
                    br.ToBus,
                    Sij.Real,
                    Sij.Imaginary,
                    Sji.Real,
                    Sji.Imaginary,
                    rateA: br.RateA / network.BaseMva
                )
            );
        }

        return flows;
    }
}
