using System.Numerics;
using PowerFlow.Core.Models;
using PowerFlow.Core.Network;
using LA = MathNet.Numerics.LinearAlgebra;

namespace PowerFlow.Core.Solver;

public class NewtonRaphsonSolver
{
    public double Tolerance { get; init; } = 1e-6; // pu — matches MATPOWER default
    public int MaxIterations { get; init; } = 50;
    public bool EnforceLimits { get; init; } = true;

    public PowerFlowResult Solve(PowerNetwork network)
    {
        var ybus = YBusBuilder.Build(network);
        int n = ybus.N;

        int slackIdx = -1;
        var pvList = new List<int>();
        var pqList = new List<int>();

        for (int i = 0; i < n; i++)
        {
            switch (network.Buses[i].Type)
            {
                case BusType.Slack:
                    slackIdx = i;
                    break;
                case BusType.PV:
                    pvList.Add(i);
                    break;
                case BusType.PQ:
                    pqList.Add(i);
                    break;
            }
        }

        if (slackIdx < 0)
            throw new InvalidOperationException("Network has no slack bus.");

        // Scheduled net injection in pu: sum of generator outputs minus load at each bus.
        var Psch = new double[n];
        var Qsch = new double[n];
        for (int i = 0; i < n; i++)
        {
            Psch[i] = -network.Buses[i].Pd / network.BaseMva;
            Qsch[i] = -network.Buses[i].Qd / network.BaseMva;
        }
        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            int i = network.IndexOf(gen.BusId);
            Psch[i] += gen.Pg / network.BaseMva;
            Qsch[i] += gen.Qg / network.BaseMva;
        }

        // Initial voltage state from bus data.
        // Va stored in radians internally; Vm stays in pu.
        var Vm = network.Buses.Select(b => b.Vm).ToArray();
        var Va = network.Buses.Select(b => b.Va * Math.PI / 180.0).ToArray();

        // Enforce generator voltage setpoints at PV and slack buses.
        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            int i = network.IndexOf(gen.BusId);
            if (network.Buses[i].Type is BusType.PV or BusType.Slack)
                Vm[i] = gen.Vg;
        }

        // Per-bus Q limits (MVAr) for PV buses; summed when multiple generators share a bus.
        // Buses without limits keep ±∞ so the comparison never fires.
        var qMaxMvar = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        var qMinMvar = Enumerable.Repeat(double.NegativeInfinity, n).ToArray();
        if (EnforceLimits)
        {
            foreach (var gen in network.Generators.Where(g => g.IsInService))
            {
                int i = network.IndexOf(gen.BusId);
                if (network.Buses[i].Type == BusType.PV)
                {
                    qMaxMvar[i] = double.IsPositiveInfinity(qMaxMvar[i])
                        ? gen.Qmax
                        : qMaxMvar[i] + gen.Qmax;
                    qMinMvar[i] = double.IsNegativeInfinity(qMinMvar[i])
                        ? gen.Qmin
                        : qMinMvar[i] + gen.Qmin;
                }
            }
        }

        double[] P = new double[n];
        double[] Q = new double[n];
        double mismatch = double.MaxValue;
        bool converged = false;
        int iterations = MaxIterations;

        bool limitViolated;
        do
        {
            limitViolated = false;

            // pvpq = all non-slack buses: PV first, then PQ (including any that were switched).
            // This ordering determines the row/column layout of the Jacobian.
            var pvpq = pvList.Concat(pqList).ToList();
            int npvpq = pvpq.Count;
            int npq = pqList.Count;
            int jDim = npvpq + npq; // Jacobian dimension: angles for pvpq + voltages for pq

            if (jDim == 0)
            {
                // Single slack-bus network — nothing to solve.
                (P, Q) = ComputeInjections(ybus, Vm, Va);
                converged = true;
                iterations = 0;
                mismatch = 0.0;
                break;
            }

            converged = false;

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                (P, Q) = ComputeInjections(ybus, Vm, Va);

                // Mismatch vector: [ΔP for pvpq buses | ΔQ for pq buses]
                var f = new double[jDim];
                for (int k = 0; k < npvpq; k++)
                    f[k] = Psch[pvpq[k]] - P[pvpq[k]];
                for (int k = 0; k < npq; k++)
                    f[npvpq + k] = Qsch[pqList[k]] - Q[pqList[k]];

                mismatch = f.Max(v => Math.Abs(v));
                if (mismatch < Tolerance)
                {
                    converged = true;
                    iterations = iter;
                    break;
                }

                var J = BuildJacobian(ybus, Vm, Va, P, Q, pvpq, pqList);
                var dx = SolveLinear(J, f);

                // Apply correction.
                // Angles: additive (Δθ in radians).
                // Voltages: scaled update — dx holds ΔV/V, so ΔV = V·(ΔV/V).
                for (int k = 0; k < npvpq; k++)
                    Va[pvpq[k]] += dx[k];
                for (int k = 0; k < npq; k++)
                    Vm[pqList[k]] += Vm[pqList[k]] * dx[npvpq + k];
            }

            if (!converged)
                break; // bail out of limit loop — report non-convergence as-is

            if (EnforceLimits)
            {
                // Check reactive limits at every bus still classified as PV.
                // Qg (MVAr) = net reactive injection × baseMVA + load reactive.
                foreach (int i in pvList.ToList())
                {
                    double qgMvar = Q[i] * network.BaseMva + network.Buses[i].Qd;
                    if (qgMvar > qMaxMvar[i])
                    {
                        Qsch[i] = (qMaxMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                        pvList.Remove(i);
                        pqList.Add(i);
                        limitViolated = true;
                    }
                    else if (qgMvar < qMinMvar[i])
                    {
                        Qsch[i] = (qMinMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                        pvList.Remove(i);
                        pqList.Add(i);
                        limitViolated = true;
                    }
                }
            }
        } while (limitViolated);

        // Net generation at each bus in pu: Pgen = net injection + load.
        // Zero for load-only buses (P[i] ≈ -Pd[i]/baseMVA → Pg[i] ≈ 0).
        var pg = new double[n];
        var qg = new double[n];
        for (int i = 0; i < n; i++)
        {
            pg[i] = P[i] + network.Buses[i].Pd / network.BaseMva;
            qg[i] = Q[i] + network.Buses[i].Qd / network.BaseMva;
        }

        return MakeResult(network, converged, iterations, Vm, Va, mismatch, pg, qg);
    }

    private static (double[] P, double[] Q) ComputeInjections(
        SparseYbus ybus,
        double[] Vm,
        double[] Va
    )
    {
        int n = ybus.N;
        var P = new double[n];
        var Q = new double[n];

        for (int i = 0; i < n; i++)
        {
            double Vi2 = Vm[i] * Vm[i];
            P[i] = ybus.Gd[i] * Vi2;
            Q[i] = -ybus.Bd[i] * Vi2;

            foreach (var (k, Gik, Bik) in ybus.OffDiag[i])
            {
                double theta = Va[i] - Va[k];
                double VV = Vm[i] * Vm[k];
                double cs = Math.Cos(theta);
                double sn = Math.Sin(theta);
                P[i] += VV * (Gik * cs + Bik * sn);
                Q[i] += VV * (Gik * sn - Bik * cs);
            }
        }

        return (P, Q);
    }

    private static double[,] BuildJacobian(
        SparseYbus ybus,
        double[] Vm,
        double[] Va,
        double[] P,
        double[] Q,
        List<int> pvpq,
        List<int> pq
    )
    {
        int npvpq = pvpq.Count;
        int npq = pq.Count;
        int m = npvpq + npq;
        var J = new double[m, m];

        // Fast lookup: bus array index → Jacobian row/col position (-1 if not present).
        var pvpqPos = new int[ybus.N];
        var pqPos = new int[ybus.N];
        Array.Fill(pvpqPos, -1);
        Array.Fill(pqPos, -1);
        for (int k = 0; k < npvpq; k++)
            pvpqPos[pvpq[k]] = k;
        for (int k = 0; k < npq; k++)
            pqPos[pq[k]] = k;

        // Off-diagonal: iterate Ybus non-zeros, fill up to 4 Jacobian entries per (i,j) pair.
        // The Jacobian has the same sparsity pattern as Ybus restricted to pvpq/pq rows and cols.
        for (int i = 0; i < ybus.N; i++)
        {
            int pr = pvpqPos[i]; // row in P-block
            int qr = pqPos[i]; // row offset in Q-block
            if (pr < 0 && qr < 0)
                continue; // slack bus — no Jacobian rows

            foreach (var (j, Gij, Bij) in ybus.OffDiag[i])
            {
                int tc = pvpqPos[j]; // col in θ-block
                int vc = pqPos[j]; // col in V-block
                if (tc < 0 && vc < 0)
                    continue; // j is slack — no Jacobian columns

                double theta = Va[i] - Va[j];
                double VV = Vm[i] * Vm[j];
                double cs = Math.Cos(theta);
                double sn = Math.Sin(theta);
                double h = VV * (Gij * sn - Bij * cs); // shared by H and L
                double nv = VV * (Gij * cs + Bij * sn); // shared by N and -M

                if (pr >= 0)
                {
                    if (tc >= 0)
                        J[pr, tc] += h; // H: ∂P_i/∂θ_j
                    if (vc >= 0)
                        J[pr, npvpq + vc] += nv; // N: Vj·∂P_i/∂Vj
                }
                if (qr >= 0)
                {
                    if (tc >= 0)
                        J[npvpq + qr, tc] -= nv; // M: ∂Q_i/∂θ_j
                    if (vc >= 0)
                        J[npvpq + qr, npvpq + vc] += h; // L: Vj·∂Q_i/∂Vj
                }
            }
        }

        // Diagonal entries use accumulated P[i]/Q[i] and the diagonal admittance.
        for (int k = 0; k < npvpq; k++)
        {
            int i = pvpq[k];
            double Vi2 = Vm[i] * Vm[i];
            J[k, k] = -Q[i] - ybus.Bd[i] * Vi2; // H_ii

            int vc = pqPos[i];
            if (vc >= 0)
                J[k, npvpq + vc] = P[i] + ybus.Gd[i] * Vi2; // N_ii
        }
        for (int k = 0; k < npq; k++)
        {
            int i = pq[k];
            double Vi2 = Vm[i] * Vm[i];
            J[npvpq + k, pvpqPos[i]] = P[i] - ybus.Gd[i] * Vi2; // M_ii
            J[npvpq + k, npvpq + k] = Q[i] - ybus.Bd[i] * Vi2; // L_ii
        }

        return J;
    }

    private static double[] SolveLinear(double[,] A, double[] b) =>
        LA.Matrix<double>.Build.DenseOfArray(A).Solve(LA.Vector<double>.Build.Dense(b)).ToArray();

    private static IReadOnlyList<BranchFlow> ComputeBranchFlows(
        PowerNetwork network,
        double[] Vm,
        double[] Va
    )
    {
        var flows = new List<BranchFlow>();

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

            // Same π model as YBusBuilder
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
                    Sji.Imaginary
                )
            );
        }

        return flows;
    }

    private static PowerFlowResult MakeResult(
        PowerNetwork network,
        bool converged,
        int iter,
        double[] Vm,
        double[] Va,
        double mismatch,
        double[] pg,
        double[] qg
    )
    {
        var flows = ComputeBranchFlows(network, Vm, Va);
        var vaDeg = Va.Select(a => a * 180.0 / Math.PI).ToArray();
        return new PowerFlowResult(
            converged,
            iter,
            mismatch,
            (double[])Vm.Clone(),
            vaDeg,
            pg,
            qg,
            flows
        );
    }
}
