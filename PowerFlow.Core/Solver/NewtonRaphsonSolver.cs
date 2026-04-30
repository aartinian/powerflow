using System.Numerics;
using CSparse;
using CSparse.Double.Factorization;
using CSparse.Storage;
using Microsoft.Extensions.Logging;
using PowerFlow.Core.Models;
using PowerFlow.Core.Network;

namespace PowerFlow.Core.Solver;

public class NewtonRaphsonSolver
{
    public double Tolerance { get; init; } = 1e-6; // pu — matches MATPOWER default
    public int MaxIterations { get; init; } = 50;
    public bool EnforceLimits { get; init; } = true;

    /// <summary>
    /// Maximum outer iterations for the Q-limit enforcement loop (PV⇄PQ switching).
    /// Caps the loop to prevent oscillation when limits are marginal.
    /// </summary>
    public int MaxLimitIterations { get; init; } = 10;

    /// <summary>
    /// When true, the initial voltage state is overridden to a flat profile
    /// (Vm = 1.0 pu, Va = 0°) regardless of what the bus data specifies.
    /// Generator Vg setpoints are still applied at PV and slack buses, so the
    /// resulting state matches what a hand-edited "flat-start" case file would
    /// produce. Useful for cold-starting from an arbitrary case file without
    /// having to maintain a separate flat-start variant.
    /// </summary>
    public bool FlatStart { get; init; } = false;

    /// <summary>
    /// When non-null, solver progress is written here — one line per NR iteration,
    /// per Q-limit switch event, and a final convergence summary.
    /// Pass any <see cref="ILogger"/> instance; wire up via DI or
    /// <c>LoggerFactory.Create(...)</c> in console apps.
    /// </summary>
    public ILogger? Log { get; init; }

    private void Info(string msg) => Log?.LogInformation("{Message}", msg);

    private void Warn(string msg) => Log?.LogWarning("{Message}", msg);

    public PowerFlowResult Solve(PowerNetwork network)
    {
        var ybus = YBusBuilder.Build(network);
        int n = ybus.N;

        var (slackIdx, pvList, pqList) = ClassifyBuses(network);
        if (slackIdx < 0)
            throw new InvalidOperationException("Network has no slack bus.");

        var (Psch, Qsch) = BuildScheduledInjections(network);
        var (Vm, Va) = BuildInitialState(network, FlatStart);
        var (qMaxMvar, qMinMvar, vgSetpoint) = BuildPvLimitsAndSetpoints(network, EnforceLimits);

        // Tracks buses that were switched PV→PQ and the reason:
        //   true  = switched because Qg hit Qmax
        //   false = switched because Qg hit Qmin
        var switchedAtMax = new Dictionary<int, bool>();

        double[] P = new double[n];
        double[] Q = new double[n];
        double mismatch = double.MaxValue;
        bool converged = false;
        int iterations = MaxIterations;
        int totalNrIters = 0;

        int inServiceBranches = network.Branches.Count(b => b.IsInService);
        Info($"> NR solve started — {n} buses, {inServiceBranches} branches");
        if (EnforceLimits)
            Info($"> Q-limit enforcement ON  (max {MaxLimitIterations} outer iters)");

        bool limitViolated;
        int limitIter = 0;
        do
        {
            limitViolated = false;
            limitIter++;
            if (EnforceLimits)
                Info($"> outer iter {limitIter, 2}/{MaxLimitIterations}");

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
                Info($">  single slack-bus network — trivially solved");
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
                Info(
                    $">  iter {iter + 1, 3}  mismatch  {mismatch:e4} pu"
                        + (mismatch < Tolerance ? " converged" : "")
                );
                if (mismatch < Tolerance)
                {
                    converged = true;
                    iterations = iter;
                    totalNrIters += iter + 1;
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
            {
                totalNrIters += MaxIterations;
                Warn(
                    $"NR did not converge after {MaxIterations} iterations  mismatch {mismatch:e2} pu"
                );
                break; // bail out of limit loop — report non-convergence as-is
            }

            if (EnforceLimits)
            {
                // 1. PQ→PV recovery: a previously switched bus whose limit is no longer binding.
                //    Qmax switch: limit not binding when Vm has risen above the setpoint.
                //    Qmin switch: limit not binding when Vm has fallen below the setpoint.
                foreach (int i in switchedAtMax.Keys.ToList())
                {
                    double vg = vgSetpoint[i];
                    bool canRecover = switchedAtMax[i] ? Vm[i] > vg : Vm[i] < vg;
                    if (canRecover)
                    {
                        string recLabel = switchedAtMax[i]
                            ? $"Vm={Vm[i]:F4} > Vg={vg:F4}  [max recovered]"
                            : $"Vm={Vm[i]:F4} < Vg={vg:F4}  [min recovered]";
                        Info($">  Q-limit: bus {network.Buses[i].Id, 4} PQ→PV  {recLabel}");
                        pqList.Remove(i);
                        pvList.Add(i);
                        Vm[i] = vg; // restore regulated voltage
                        switchedAtMax.Remove(i);
                        limitViolated = true;
                    }
                }

                // 2. PV→PQ switching: check reactive limits on still-PV buses.
                //    Qg (MVAr) = net reactive injection × baseMVA + load reactive.
                foreach (int i in pvList.ToList())
                {
                    double qgMvar = Q[i] * network.BaseMva + network.Buses[i].Qd;
                    if (qgMvar > qMaxMvar[i])
                    {
                        Info(
                            $">  Q-limit: bus {network.Buses[i].Id, 4} PV→PQ"
                                + $"  Qg={qgMvar:F1} > Qmax={qMaxMvar[i]:F1} MVAr  [ceiling]"
                        );
                        Qsch[i] = (qMaxMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                        pvList.Remove(i);
                        pqList.Add(i);
                        switchedAtMax[i] = true;
                        limitViolated = true;
                    }
                    else if (qgMvar < qMinMvar[i])
                    {
                        Info(
                            $">  Q-limit: bus {network.Buses[i].Id, 4} PV→PQ"
                                + $"  Qg={qgMvar:F1} < Qmin={qMinMvar[i]:F1} MVAr  [floor]"
                        );
                        Qsch[i] = (qMinMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                        pvList.Remove(i);
                        pqList.Add(i);
                        switchedAtMax[i] = false;
                        limitViolated = true;
                    }
                }
            }
            if (EnforceLimits && !limitViolated)
                Info($">  Q-limit: no violations → done");
        } while (limitViolated && limitIter < MaxLimitIterations);

        if (converged && limitViolated)
        {
            Warn(
                $"Q-limit outer loop capped at {MaxLimitIterations} iters"
                    + " — result may not satisfy all reactive limits"
            );
            foreach (var (busIdx, atMax) in switchedAtMax)
            {
                double qgMvar =
                    (Q[busIdx] + network.Buses[busIdx].Qd / network.BaseMva) * network.BaseMva;
                int busId = network.Buses[busIdx].Id;
                if (atMax)
                    Warn(
                        $">  unsatisfied Q-limit: bus {busId, 4}"
                            + $"  Qg={qgMvar:F1} MVAr  limit=Qmax={qMaxMvar[busIdx]:F1} MVAr"
                    );
                else
                    Warn(
                        $">  unsatisfied Q-limit: bus {busId, 4}"
                            + $"  Qg={qgMvar:F1} MVAr  limit=Qmin={qMinMvar[busIdx]:F1} MVAr"
                    );
            }
        }
        string outerInfo = EnforceLimits
            ? $"  {limitIter} outer iter{(limitIter != 1 ? "s" : "")}"
            : "";
        if (converged)
            Info(
                $"> result: converged"
                    + $"  {totalNrIters} NR iter{(totalNrIters != 1 ? "s" : "")}"
                    + $"{outerInfo}  mismatch {mismatch:e2} pu"
            );
        else
            Warn($"result: NOT converged  mismatch {mismatch:e2} pu");

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

    // ── Setup helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Splits buses into slack / PV / PQ index lists. Returns slackIdx = -1 when
    /// no slack bus is present (caller is expected to throw).
    /// </summary>
    private static (int slackIdx, List<int> pvList, List<int> pqList) ClassifyBuses(
        PowerNetwork network
    )
    {
        int slackIdx = -1;
        var pvList = new List<int>();
        var pqList = new List<int>();

        for (int i = 0; i < network.Buses.Count; i++)
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

        return (slackIdx, pvList, pqList);
    }

    /// <summary>
    /// Scheduled net injection in pu at every bus: Σ generator dispatch − load.
    /// </summary>
    private static (double[] Psch, double[] Qsch) BuildScheduledInjections(PowerNetwork network)
    {
        int n = network.Buses.Count;
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

        return (Psch, Qsch);
    }

    /// <summary>
    /// Initial voltage state. With <paramref name="flatStart"/> = false (default),
    /// Vm/Va come from the bus data; with flatStart = true, all buses are
    /// initialised to Vm = 1.0 pu and Va = 0. Either way, generator Vg
    /// setpoints are applied at PV and slack buses afterwards. Va is returned
    /// in radians.
    /// </summary>
    private static (double[] Vm, double[] Va) BuildInitialState(
        PowerNetwork network,
        bool flatStart
    )
    {
        int n = network.Buses.Count;
        double[] Vm;
        double[] Va;
        if (flatStart)
        {
            Vm = Enumerable.Repeat(1.0, n).ToArray();
            Va = new double[n];
        }
        else
        {
            Vm = network.Buses.Select(b => b.Vm).ToArray();
            Va = network.Buses.Select(b => b.Va * Math.PI / 180.0).ToArray();
        }

        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            int i = network.IndexOf(gen.BusId);
            if (network.Buses[i].Type is BusType.PV or BusType.Slack)
                Vm[i] = gen.Vg;
        }

        return (Vm, Va);
    }

    /// <summary>
    /// Per-bus Q-limits (MVAr) and Vg setpoints for PV buses. Multiple generators
    /// on the same bus contribute additively to the limits; the last gen's Vg wins.
    /// Buses without PV-bus generators keep ±∞ for limits and 0 for setpoint.
    /// When <paramref name="enforceLimits"/> is false the limit arrays stay at ±∞.
    /// </summary>
    private static (
        double[] qMaxMvar,
        double[] qMinMvar,
        double[] vgSetpoint
    ) BuildPvLimitsAndSetpoints(PowerNetwork network, bool enforceLimits)
    {
        int n = network.Buses.Count;
        var qMaxMvar = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        var qMinMvar = Enumerable.Repeat(double.NegativeInfinity, n).ToArray();
        var vgSetpoint = new double[n];

        foreach (var gen in network.Generators.Where(g => g.IsInService))
        {
            int i = network.IndexOf(gen.BusId);
            if (network.Buses[i].Type != BusType.PV)
                continue;

            if (enforceLimits)
            {
                qMaxMvar[i] = double.IsPositiveInfinity(qMaxMvar[i])
                    ? gen.Qmax
                    : qMaxMvar[i] + gen.Qmax;
                qMinMvar[i] = double.IsNegativeInfinity(qMinMvar[i])
                    ? gen.Qmin
                    : qMinMvar[i] + gen.Qmin;
            }

            vgSetpoint[i] = gen.Vg;
        }

        return (qMaxMvar, qMinMvar, vgSetpoint);
    }

    // ── Numerical core ────────────────────────────────────────────────────────

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

    private static CompressedColumnStorage<double> BuildJacobian(
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

        // Fast lookup: bus array index → Jacobian row/col position (-1 if not present).
        var pvpqPos = new int[ybus.N];
        var pqPos = new int[ybus.N];
        Array.Fill(pvpqPos, -1);
        Array.Fill(pqPos, -1);
        for (int k = 0; k < npvpq; k++)
            pvpqPos[pvpq[k]] = k;
        for (int k = 0; k < npq; k++)
            pqPos[pq[k]] = k;

        // Capacity: each Ybus off-diagonal non-zero produces ≤4 J entries; diagonals add m+npq.
        int capacity = ybus.OffDiag.Sum(r => r.Length) * 4 + m + npq * 2;
        var triplets = new CoordinateStorage<double>(m, m, capacity);

        // Off-diagonal: iterate Ybus non-zeros, emit up to 4 Jacobian entries per (i,j) pair.
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
                        triplets.At(pr, tc, h); // H: ∂P_i/∂θ_j
                    if (vc >= 0)
                        triplets.At(pr, npvpq + vc, nv); // N: Vj·∂P_i/∂Vj
                }
                if (qr >= 0)
                {
                    if (tc >= 0)
                        triplets.At(npvpq + qr, tc, -nv); // M: ∂Q_i/∂θ_j
                    if (vc >= 0)
                        triplets.At(npvpq + qr, npvpq + vc, h); // L: Vj·∂Q_i/∂Vj
                }
            }
        }

        // Diagonal entries use accumulated P[i]/Q[i] and the diagonal admittance.
        for (int k = 0; k < npvpq; k++)
        {
            int i = pvpq[k];
            double Vi2 = Vm[i] * Vm[i];
            triplets.At(k, k, -Q[i] - ybus.Bd[i] * Vi2); // H_ii

            int vc = pqPos[i];
            if (vc >= 0)
                triplets.At(k, npvpq + vc, P[i] + ybus.Gd[i] * Vi2); // N_ii
        }
        for (int k = 0; k < npq; k++)
        {
            int i = pq[k];
            double Vi2 = Vm[i] * Vm[i];
            triplets.At(npvpq + k, pvpqPos[i], P[i] - ybus.Gd[i] * Vi2); // M_ii
            triplets.At(npvpq + k, npvpq + k, Q[i] - ybus.Bd[i] * Vi2); // L_ii
        }

        // sumDuplicates=true handles parallel branches.
        return CompressedColumnStorage<double>.OfIndexed(triplets, true);
    }

    private static double[] SolveLinear(CompressedColumnStorage<double> A, double[] b)
    {
        var x = new double[b.Length];
        SparseLU.Create(A, ColumnOrdering.MinimumDegreeAtPlusA, 1.0).Solve(b, x);
        return x;
    }

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
                    Sji.Imaginary,
                    rateA: br.RateA / network.BaseMva
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

        // Voltage violations are only meaningful when the solver converged.
        var violations = new List<VoltageViolation>();
        if (converged)
        {
            for (int i = 0; i < network.Buses.Count; i++)
            {
                var bus = network.Buses[i];
                if (Vm[i] < bus.Vmin || Vm[i] > bus.Vmax)
                    violations.Add(new VoltageViolation(bus.Id, Vm[i], bus.Vmin, bus.Vmax));
            }
        }

        return new PowerFlowResult(
            converged,
            iter,
            mismatch,
            (double[])Vm.Clone(),
            vaDeg,
            pg,
            qg,
            flows,
            violations
        );
    }
}
