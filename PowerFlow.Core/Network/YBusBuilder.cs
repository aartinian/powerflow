using System.Numerics;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Network;

public static class YBusBuilder
{
    public static Complex[,] Build(PowerNetwork network)
    {
        int n = network.Buses.Count;
        var Y = new Complex[n, n];

        foreach (var br in network.Branches)
        {
            if (!br.IsInService)
                continue;

            int i = network.IndexOf(br.FromBus);
            int j = network.IndexOf(br.ToBus);

            var ys = 1.0 / new Complex(br.R, br.X); // series admittance
            var yc = new Complex(0, br.B / 2.0); // half line charging

            // Complex tap: t = |a|·e^(jφ). For plain lines TapRatio=1 and PhaseShift=0,
            // so t=1 and the stamp below reduces to the symmetric transmission-line model.
            double phi = br.PhaseShift * Math.PI / 180.0;
            var t = new Complex(br.TapRatio * Math.Cos(phi), br.TapRatio * Math.Sin(phi));
            double tMagSq = br.TapRatio * br.TapRatio;

            // Off-nominal tap transformer π model
            // Diagonal asymmetry (÷|t|² on from-side) arises because the tap is on the from bus.
            Y[i, i] += (ys + yc) / tMagSq;
            Y[j, j] += ys + yc;
            Y[i, j] -= ys / Complex.Conjugate(t);
            Y[j, i] -= ys / t;
        }

        // Gs/Bs are stored in MW/MVAr (see Bus.cs); divide by baseMVA to get pu admittance.
        foreach (var bus in network.Buses)
        {
            int i = network.IndexOf(bus.Id);
            Y[i, i] += new Complex(bus.Gs, bus.Bs) / network.BaseMva;
        }

        return Y;
    }
}
