using MathNet.Numerics.LinearAlgebra;
using PowerFlow.Core.Models;
using PowerFlow.Core.Network;

namespace PowerFlow.Core.Solver;

public class NewtonRaphsonSolver
{
    public double Tolerance { get; init; } = 1e-6; // pu — matches MATPOWER default
    public int MaxIterations { get; init; } = 50;

    public PowerFlowResult Solve(PowerNetwork network)
    {
        var Ybus = YBusBuilder.Build(network);
        int n = network.Buses.Count;

        // Cache real and imaginary parts separately
        var G = new double[n, n];
        var B = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            G[i, j] = Ybus[i, j].Real;
            B[i, j] = Ybus[i, j].Imaginary;
        }

        // Classify buses into slack / PV / PQ.
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

        // pvpq = all non-slack buses: PV first, then PQ.
        // This ordering determines the row/column layout of the Jacobian.
        var pvpq = pvList.Concat(pqList).ToList();
        int npvpq = pvpq.Count;
        int npq = pqList.Count;
        int jDim = npvpq + npq; // Jacobian dimension: angles for pvpq + voltages for pq

        if (jDim == 0)
            return MakeResult(true, 0, new double[n], new double[n], 0.0); // single slack bus

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
        // Bus.Vm from the case file may differ from Vg if the file stores solved results.
        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            int i = network.IndexOf(gen.BusId);
            if (network.Buses[i].Type is BusType.PV or BusType.Slack)
                Vm[i] = gen.Vg;
        }

        double mismatch = double.MaxValue;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            var (P, Q) = ComputeInjections(G, B, Vm, Va, n);

            // Mismatch vector: [ΔP for pvpq buses | ΔQ for pq buses]
            var f = new double[jDim];
            for (int k = 0; k < npvpq; k++)
                f[k] = Psch[pvpq[k]] - P[pvpq[k]];
            for (int k = 0; k < npq; k++)
                f[npvpq + k] = Qsch[pqList[k]] - Q[pqList[k]];

            mismatch = f.Max(v => Math.Abs(v));
            if (mismatch < Tolerance)
                return MakeResult(true, iter, Vm, Va, mismatch);

            var J = BuildJacobian(G, B, Vm, Va, P, Q, pvpq, pqList);
            var dx = SolveLinear(J, f);

            // Apply correction.
            // Angles: additive (Δθ in radians).
            // Voltages: scaled update — dx holds ΔV/V, so ΔV = V·(ΔV/V).
            for (int k = 0; k < npvpq; k++)
                Va[pvpq[k]] += dx[k];
            for (int k = 0; k < npq; k++)
                Vm[pqList[k]] += Vm[pqList[k]] * dx[npvpq + k];
        }

        return MakeResult(false, MaxIterations, Vm, Va, mismatch);
    }

    private static (double[] P, double[] Q) ComputeInjections(
        double[,] G,
        double[,] B,
        double[] Vm,
        double[] Va,
        int n
    )
    {
        var P = new double[n];
        var Q = new double[n];

        for (int i = 0; i < n; i++)
        for (int k = 0; k < n; k++)
        {
            double theta = Va[i] - Va[k];
            double VV = Vm[i] * Vm[k];
            double cs = Math.Cos(theta);
            double sn = Math.Sin(theta);
            P[i] += VV * (G[i, k] * cs + B[i, k] * sn);
            Q[i] += VV * (G[i, k] * sn - B[i, k] * cs);
        }

        return (P, Q);
    }

    private static double[,] BuildJacobian(
        double[,] G,
        double[,] B,
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

        // The Jacobian uses the scaled formulation: N = V·∂P/∂V, L = V·∂Q/∂V.
        // This gives the cleaner diagonal formulas below and pairs with the V·e voltage update.
        //
        // Block layout:
        //   [H  N]   rows: pvpq for P eqs,  pvpq for P eqs
        //   [M  L]   rows: pq   for Q eqs,  pq   for Q eqs
        //            cols: pvpq for Δθ,      pq   for ΔV/V
        for (int r = 0; r < m; r++)
        {
            bool pRow = r < npvpq;
            int i = pRow ? pvpq[r] : pq[r - npvpq];

            for (int c = 0; c < m; c++)
            {
                bool thetaCol = c < npvpq;
                int j = thetaCol ? pvpq[c] : pq[c - npvpq];

                double Vi = Vm[i];
                double Vj = Vm[j];
                double Gij = G[i, j];
                double Bij = B[i, j];
                double theta = Va[i] - Va[j];

                if (i != j)
                {
                    double VV = Vi * Vj;
                    double cs = Math.Cos(theta);
                    double sn = Math.Sin(theta);

                    J[r, c] = (pRow, thetaCol) switch
                    {
                        (true, true) => VV * (Gij * sn - Bij * cs),   // H: ∂P_i/∂θ_j
                        (true, false) => VV * (Gij * cs + Bij * sn),  // N: Vj·∂P_i/∂Vj
                        (false, true) => -VV * (Gij * cs + Bij * sn), // M: ∂Q_i/∂θ_j
                        (false, false) => VV * (Gij * sn - Bij * cs), // L: Vj·∂Q_i/∂Vj
                    };
                }
                else
                {
                    double Vi2 = Vi * Vi;

                    J[r, c] = (pRow, thetaCol) switch
                    {
                        (true, true) => -Q[i] - B[i, i] * Vi2,  // H_ii
                        (true, false) => P[i] + G[i, i] * Vi2,  // N_ii
                        (false, true) => P[i] - G[i, i] * Vi2,  // M_ii
                        (false, false) => Q[i] - B[i, i] * Vi2, // L_ii
                    };
                }
            }
        }

        return J;
    }

    private static double[] SolveLinear(double[,] A, double[] b) =>
        Matrix<double>.Build.DenseOfArray(A).Solve(Vector<double>.Build.Dense(b)).ToArray();

    private static PowerFlowResult MakeResult(
        bool converged,
        int iter,
        double[] Vm,
        double[] Va,
        double mismatch
    )
    {
        var vaDeg = Va.Select(a => a * 180.0 / Math.PI).ToArray();
        return new PowerFlowResult(converged, iter, mismatch, (double[])Vm.Clone(), vaDeg);
    }
}
