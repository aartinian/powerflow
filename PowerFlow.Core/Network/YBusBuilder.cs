using System.Numerics;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Network;

public static class YBusBuilder
{
    public static SparseYbus Build(PowerNetwork network)
    {
        int n = network.Buses.Count;
        var gd = new double[n];
        var bd = new double[n];
        var rows = new List<(int J, double G, double B)>[n];
        for (int i = 0; i < n; i++)
            rows[i] = [];

        foreach (var br in network.Branches)
        {
            if (!br.IsInService)
                continue;

            int i = network.IndexOf(br.FromBus);
            int j = network.IndexOf(br.ToBus);

            var ys = 1.0 / new Complex(br.R, br.X);
            var yc = new Complex(0, br.B / 2.0);

            double phi = br.PhaseShift * Math.PI / 180.0;
            var t = new Complex(br.TapRatio * Math.Cos(phi), br.TapRatio * Math.Sin(phi));
            double tMagSq = br.TapRatio * br.TapRatio;

            // Off-nominal tap transformer π model
            var yii = (ys + yc) / tMagSq;
            var yjj = ys + yc;
            var yij = -(ys / Complex.Conjugate(t));
            var yji = -(ys / t);

            gd[i] += yii.Real;
            bd[i] += yii.Imaginary;
            gd[j] += yjj.Real;
            bd[j] += yjj.Imaginary;
            rows[i].Add((j, yij.Real, yij.Imaginary));
            rows[j].Add((i, yji.Real, yji.Imaginary));
        }

        // Gs/Bs are stored in MW/MVAr; divide by baseMVA to get pu admittance.
        foreach (var bus in network.Buses)
        {
            int i = network.IndexOf(bus.Id);
            gd[i] += bus.Gs / network.BaseMva;
            bd[i] += bus.Bs / network.BaseMva;
        }

        return new SparseYbus(n, gd, bd, rows.Select(l => l.ToArray()).ToArray());
    }
}
