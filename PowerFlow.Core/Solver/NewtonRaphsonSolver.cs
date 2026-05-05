using CSparse;
using CSparse.Double.Factorization;
using CSparse.Storage;
using Microsoft.Extensions.Logging;
using PowerFlow.Core.Models;
using PowerFlow.Core.Network;

namespace PowerFlow.Core.Solver;

/// <summary>
/// AC power-flow solver using polar-form Newton-Raphson with a sparse LU
/// kernel (CSparse). Configure with init-only properties (<see cref="Tolerance"/>,
/// <see cref="MaxIterations"/>, <see cref="EnforceLimits"/>, <see cref="FlatStart"/>,
/// <see cref="MaxLimitIterations"/>, <see cref="Log"/>) and call
/// <see cref="Solve"/>. The solver is stateless between calls; reuse a single
/// instance or create a new one freely.
/// </summary>
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
    /// Maximum number of step halvings in the backtracking line search applied
    /// after each Newton-Raphson correction. The solver tries μ = 1, ½, ¼, …,
    /// 2<sup>−MaxStepHalvings</sup> and accepts the first μ that reduces ‖f‖∞.
    /// Set to 0 to disable line search (pure Newton, μ = 1 always). Default 10
    /// matches MATPOWER. For well-conditioned networks μ = 1 is accepted on the
    /// first try; the search adds negligible overhead in the common case.
    /// </summary>
    public int MaxStepHalvings { get; init; } = 10;

    /// <summary>
    /// When true the real-power imbalance is shared across all in-service generators
    /// via an augmented Newton-Raphson system (one extra variable λ and one extra equation
    /// for the reference-bus real-power balance). Participation factors α are proportional
    /// to each generator's Pmax; the real-power injection at bus i shifts by α_i · λ (pu)
    /// from the scheduled dispatch. λ is returned in
    /// <see cref="PowerFlowResult.Lambda"/>.
    /// Default false (conventional single slack-bus formulation).
    /// </summary>
    public bool DistributedSlack { get; init; } = false;

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

    /// <summary>
    /// Solve the AC power flow for the given network using polar-form
    /// Newton-Raphson with sparse LU factorisation. The network must contain
    /// exactly one slack bus; validate first with <see cref="NetworkValidator"/>
    /// to surface other issues (missing bus references, bad tap ratios, etc.)
    /// before calling.
    /// </summary>
    /// <remarks>
    /// When <see cref="EnforceLimits"/> is on, an outer loop switches PV↔PQ
    /// buses as their reactive output crosses generator Q-limits, capped at
    /// <see cref="MaxLimitIterations"/>. When <see cref="FlatStart"/> is on,
    /// the initial Vm/Va comes from a flat profile rather than the bus data.
    /// The returned result is always non-null; check
    /// <see cref="PowerFlowResult.Converged"/> to know whether the solution
    /// is trustworthy.
    /// </remarks>
    public PowerFlowResult Solve(PowerNetwork network)
    {
        var ybus = YBusBuilder.Build(network);
        int n = ybus.N;

        var (slackIdx, pvList, pqList) = ClassifyBuses(network);
        if (slackIdx < 0)
            throw new InvalidOperationException("Network has no slack bus.");

        var (Psch, Qsch) = BuildScheduledInjections(network);
        var alpha = DistributedSlack ? BuildParticipationFactors(network) : Array.Empty<double>();
        double lambda = 0.0;
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
        int isolatedCount = network.Buses.Count(b => b.Type == BusType.Isolated);
        string isoNote = isolatedCount > 0 ? $" ({isolatedCount} isolated)" : "";
        Info($"NR solve started — {n} buses{isoNote}, {inServiceBranches} branches");
        if (EnforceLimits)
            Info($"Q-limit enforcement ON  (max {MaxLimitIterations} outer iters)");
        if (DistributedSlack)
            Info($"Distributed slack ON  ({alpha.Count(a => a > 0)} participating bus(es))");

        bool limitViolated = false;
        int limitIter = 0;

        // Single slack-bus network: no NR equations to solve, just compute injections.
        // Q-limit switching can never reduce pvList+pqList to empty, so this is invariant.
        if (pvList.Count + pqList.Count == 0)
        {
            Info($"single slack-bus network — trivially solved");
            (P, Q) = ComputeInjections(ybus, Vm, Va);
            converged = true;
            iterations = 0;
            mismatch = 0.0;
        }
        else
        {
            do
            {
                limitViolated = false;
                limitIter++;
                if (EnforceLimits)
                    Info($"outer iter {limitIter, 2}/{MaxLimitIterations}");

                // pvpq = all non-slack buses: PV first, then PQ (including any that were switched).
                // This ordering determines the row/column layout of the Jacobian.
                var pvpq = pvList.Concat(pqList).ToList();
                int npvpq = pvpq.Count;
                int npq = pqList.Count;
                int jDim = npvpq + npq + (DistributedSlack ? 1 : 0); // [θ_pvpq | V_pq | λ]

                converged = false;

                for (int iter = 0; iter < MaxIterations; iter++)
                {
                    (P, Q) = ComputeInjections(ybus, Vm, Va);

                    // Mismatch vector: [ΔP for pvpq buses | ΔQ for pq buses | ΔP for slack bus]
                    // With distributed slack each ΔP_k carries the participation term α_k·λ.
                    var f = new double[jDim];
                    for (int k = 0; k < npvpq; k++)
                        f[k] =
                            Psch[pvpq[k]]
                            + (DistributedSlack ? alpha[pvpq[k]] * lambda : 0.0)
                            - P[pvpq[k]];
                    for (int k = 0; k < npq; k++)
                        f[npvpq + k] = Qsch[pqList[k]] - Q[pqList[k]];
                    if (DistributedSlack)
                        f[jDim - 1] = Psch[slackIdx] + alpha[slackIdx] * lambda - P[slackIdx];

                    mismatch = f.Max(v => Math.Abs(v));
                    Info(
                        $"  iter {iter + 1, 3}  mismatch  {mismatch:e4} pu"
                            + (mismatch < Tolerance ? " converged" : "")
                    );
                    if (mismatch < Tolerance)
                    {
                        converged = true;
                        iterations = iter;
                        totalNrIters += iter + 1;
                        break;
                    }

                    var J = BuildJacobian(
                        ybus,
                        Vm,
                        Va,
                        P,
                        Q,
                        pvpq,
                        pqList,
                        DistributedSlack ? slackIdx : -1,
                        DistributedSlack ? alpha : null
                    );
                    var dx = SolveLinear(J, f);

                    // Backtracking line search: find the largest μ = 2^{−k} that
                    // reduces ‖f‖∞. For well-conditioned networks μ = 1 on the first
                    // try; the search adds negligible overhead in the common case.
                    double mu = FindStepSize(
                        ybus,
                        Vm,
                        Va,
                        Psch,
                        Qsch,
                        pvpq,
                        pqList,
                        dx,
                        mismatch,
                        npvpq,
                        lambda,
                        DistributedSlack ? alpha : null,
                        slackIdx
                    );
                    if (mu < 1.0)
                        Info($"    step limited: μ = {mu:F4}");

                    // Apply the μ-scaled correction.
                    // Angles: Δθ (rad) added directly.
                    // Voltages: Vᵢ_new = Vᵢ_old · (1 + μ · ΔV/V).
                    for (int k = 0; k < npvpq; k++)
                        Va[pvpq[k]] += mu * dx[k];
                    for (int k = 0; k < npq; k++)
                        Vm[pqList[k]] *= 1.0 + mu * dx[npvpq + k];
                    if (DistributedSlack)
                        lambda += mu * dx[jDim - 1];
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
                    limitViolated = ApplyQLimitSwitches(
                        network,
                        Vm,
                        Q,
                        Qsch,
                        pvList,
                        pqList,
                        switchedAtMax,
                        vgSetpoint,
                        qMaxMvar,
                        qMinMvar
                    );
                    if (!limitViolated)
                        Info($"  Q-limit: no violations → done");
                }
            } while (limitViolated && limitIter < MaxLimitIterations);
        }

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
                        $"  unsatisfied Q-limit: bus {busId, 4}"
                            + $"  Qg={qgMvar:F1} MVAr  limit=Qmax={qMaxMvar[busIdx]:F1} MVAr"
                    );
                else
                    Warn(
                        $"  unsatisfied Q-limit: bus {busId, 4}"
                            + $"  Qg={qgMvar:F1} MVAr  limit=Qmin={qMinMvar[busIdx]:F1} MVAr"
                    );
            }
        }
        string outerInfo = EnforceLimits
            ? $"  {limitIter} outer iter{(limitIter != 1 ? "s" : "")}"
            : "";
        if (converged)
        {
            Info(
                $"result: converged"
                    + $"  {totalNrIters} NR iter{(totalNrIters != 1 ? "s" : "")}"
                    + $"{outerInfo}  mismatch {mismatch:e2} pu"
            );
            if (DistributedSlack)
                Info($"  distributed slack λ = {lambda:F6} pu");
        }
        else
            Warn($"result: NOT converged  mismatch {mismatch:e2} pu");

        // Net generation at each bus in pu: Pgen = net injection + load.
        // Zero for load-only buses (P[i] ≈ -Pd[i]/baseMVA → Pg[i] ≈ 0).
        // Isolated buses have no Y-bus connections so P[i] = Q[i] = 0; their
        // load data is not served and must not appear as spurious generation.
        var pg = new double[n];
        var qg = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (network.Buses[i].Type == BusType.Isolated)
                continue; // pg[i] = qg[i] stay 0
            pg[i] = P[i] + network.Buses[i].Pd / network.BaseMva;
            qg[i] = Q[i] + network.Buses[i].Qd / network.BaseMva;
        }

        return MakeResult(network, converged, iterations, Vm, Va, mismatch, pg, qg, lambda);
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

    /// <summary>
    /// Builds the normalised participation-factor vector α (one entry per bus, indexed
    /// by network.Buses order). α_i is proportional to the sum of Pmax across all
    /// in-service generators at bus i; buses with no eligible generator receive α_i = 0.
    /// When no generator has Pmax &gt; 0 (degenerate case), α falls back to a uniform
    /// 1/n distribution so the augmented Jacobian remains non-singular.
    /// </summary>
    private static double[] BuildParticipationFactors(PowerNetwork network)
    {
        int n = network.Buses.Count;
        var alpha = new double[n];
        foreach (var gen in network.Generators.Where(g => g.IsInService && g.Pmax > 0))
            alpha[network.IndexOf(gen.BusId)] += gen.Pmax;
        double total = alpha.Sum();
        if (total > 0)
            for (int i = 0; i < n; i++)
                alpha[i] /= total;
        else
            for (int i = 0; i < n; i++)
                alpha[i] = 1.0 / n;
        return alpha;
    }

    // ── Q-limit enforcement ───────────────────────────────────────────────────

    /// <summary>
    /// One sweep of Q-limit enforcement after an inner-NR convergence:
    /// (1) recover PQ→PV for previously-switched buses whose Vm has crossed back
    ///     past the setpoint (Vm &gt; Vg for Qmax-switched, Vm &lt; Vg for Qmin),
    ///     restoring the regulated voltage; and
    /// (2) switch PV→PQ for any remaining PV bus whose computed Qg has crossed a
    ///     reactive limit, pinning Qsch at the binding limit.
    /// Mutates <paramref name="pvList"/>, <paramref name="pqList"/>,
    /// <paramref name="Qsch"/>, <paramref name="Vm"/>, and
    /// <paramref name="switchedAtMax"/> in place.
    /// </summary>
    /// <returns>
    /// True if any bus was switched or recovered (caller should re-run the inner NR);
    /// false if no Q-limits are binding and the outer loop can terminate.
    /// </returns>
    private bool ApplyQLimitSwitches(
        PowerNetwork network,
        double[] Vm,
        double[] Q,
        double[] Qsch,
        List<int> pvList,
        List<int> pqList,
        Dictionary<int, bool> switchedAtMax,
        double[] vgSetpoint,
        double[] qMaxMvar,
        double[] qMinMvar
    )
    {
        bool changed = false;

        // 1. PQ→PV recovery: a previously switched bus whose limit is no longer binding.
        //    Qmax switch: limit not binding when Vm has risen above the setpoint.
        //    Qmin switch: limit not binding when Vm has fallen below the setpoint.
        foreach (int i in switchedAtMax.Keys.ToList())
        {
            double vg = vgSetpoint[i];
            bool canRecover = switchedAtMax[i] ? Vm[i] > vg : Vm[i] < vg;
            if (!canRecover)
                continue;

            string recLabel = switchedAtMax[i]
                ? $"Vm={Vm[i]:F4} > Vg={vg:F4}  [max recovered]"
                : $"Vm={Vm[i]:F4} < Vg={vg:F4}  [min recovered]";
            Info($"  Q-limit: bus {network.Buses[i].Id, 4} PQ→PV  {recLabel}");
            pqList.Remove(i);
            pvList.Add(i);
            Vm[i] = vg; // restore regulated voltage
            switchedAtMax.Remove(i);
            changed = true;
        }

        // 2. PV→PQ switching: check reactive limits on still-PV buses.
        //    Qg (MVAr) = net reactive injection × baseMVA + load reactive.
        foreach (int i in pvList.ToList())
        {
            double qgMvar = Q[i] * network.BaseMva + network.Buses[i].Qd;
            if (qgMvar > qMaxMvar[i])
            {
                Info(
                    $"  Q-limit: bus {network.Buses[i].Id, 4} PV→PQ"
                        + $"  Qg={qgMvar:F1} > Qmax={qMaxMvar[i]:F1} MVAr  [ceiling]"
                );
                Qsch[i] = (qMaxMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                pvList.Remove(i);
                pqList.Add(i);
                switchedAtMax[i] = true;
                changed = true;
            }
            else if (qgMvar < qMinMvar[i])
            {
                Info(
                    $"  Q-limit: bus {network.Buses[i].Id, 4} PV→PQ"
                        + $"  Qg={qgMvar:F1} < Qmin={qMinMvar[i]:F1} MVAr  [floor]"
                );
                Qsch[i] = (qMinMvar[i] - network.Buses[i].Qd) / network.BaseMva;
                pvList.Remove(i);
                pqList.Add(i);
                switchedAtMax[i] = false;
                changed = true;
            }
        }

        return changed;
    }

    // ── Numerical core ────────────────────────────────────────────────────────

    /// <summary>
    /// Backtracking line search over μ = 1, ½, ¼, …, 2<sup>−MaxStepHalvings</sup>.
    /// Evaluates the mismatch at the tentative point x + μ·dx and returns the first
    /// μ that reduces ‖f‖∞ below <paramref name="currentMismatch"/>. When
    /// <see cref="MaxStepHalvings"/> is 0 the search is disabled and 1.0 is returned
    /// immediately. Falls through to the smallest tried μ when no halvings help —
    /// the caller's convergence check then decides whether to declare failure.
    /// </summary>
    private double FindStepSize(
        SparseYbus ybus,
        double[] Vm,
        double[] Va,
        double[] Psch,
        double[] Qsch,
        List<int> pvpq,
        List<int> pqList,
        double[] dx,
        double currentMismatch,
        int npvpq,
        double lambda = 0,
        double[]? alpha = null,
        int slackIdx = -1
    )
    {
        if (MaxStepHalvings == 0)
            return 1.0;

        bool distSlack = alpha is not null && slackIdx >= 0;
        int n = Vm.Length;
        int npq = pqList.Count;
        int dxDim = npvpq + npq + (distSlack ? 1 : 0);
        var VmTry = new double[n];
        var VaTry = new double[n];

        for (int h = 0; h <= MaxStepHalvings; h++)
        {
            double mu = Math.Pow(0.5, h); // 1, ½, ¼, …, 2^{−MaxStepHalvings}
            double lambdaTry = distSlack ? lambda + mu * dx[dxDim - 1] : 0;

            Array.Copy(Vm, VmTry, n);
            Array.Copy(Va, VaTry, n);
            for (int k = 0; k < npvpq; k++)
                VaTry[pvpq[k]] += mu * dx[k];
            for (int k = 0; k < npq; k++)
                VmTry[pqList[k]] *= 1.0 + mu * dx[npvpq + k];

            var (Ptry, Qtry) = ComputeInjections(ybus, VmTry, VaTry);

            double tryMismatch = 0.0;
            for (int k = 0; k < pvpq.Count; k++)
            {
                double pRef = Psch[pvpq[k]] + (distSlack ? alpha![pvpq[k]] * lambdaTry : 0.0);
                tryMismatch = Math.Max(tryMismatch, Math.Abs(pRef - Ptry[pvpq[k]]));
            }
            foreach (int i in pqList)
                tryMismatch = Math.Max(tryMismatch, Math.Abs(Qsch[i] - Qtry[i]));
            if (distSlack)
            {
                double pSlack = Psch[slackIdx] + alpha![slackIdx] * lambdaTry;
                tryMismatch = Math.Max(tryMismatch, Math.Abs(pSlack - Ptry[slackIdx]));
            }

            if (tryMismatch < currentMismatch)
                return mu;
        }

        // No halving improved the mismatch; return the smallest tried step.
        return Math.Pow(0.5, MaxStepHalvings);
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

    private static CompressedColumnStorage<double> BuildJacobian(
        SparseYbus ybus,
        double[] Vm,
        double[] Va,
        double[] P,
        double[] Q,
        List<int> pvpq,
        List<int> pq,
        int slackIdx = -1, // ≥ 0 to enable distributed-slack augmentation
        double[]? alpha = null
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

        bool distSlack = slackIdx >= 0 && alpha is not null;
        int dim = distSlack ? m + 1 : m;
        // Capacity: each Ybus off-diagonal non-zero produces ≤4 J entries; diagonals add m+npq.
        // Distributed slack adds the λ column (npvpq entries), reference-bus row
        // (≤2·deg(slack) off-diagonal entries), and one corner entry.
        int slackDeg = distSlack ? ybus.OffDiag[slackIdx].Length : 0;
        int capacity =
            ybus.OffDiag.Sum(r => r.Length) * 4
            + m
            + npq * 2
            + (distSlack ? npvpq + 2 * slackDeg + 1 : 0);
        var triplets = new CoordinateStorage<double>(dim, dim, capacity);

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

        // Distributed slack: augment with a λ column and a reference-bus P row.
        if (distSlack)
        {
            // λ column (col m): J_pf[k,m] = −∂f_k/∂λ = −α[pvpq[k]].
            // (∂f_k/∂λ = +α because f_k = Psch_k + α_k·λ − P_k, so J_pf = −∂f/∂x uses −α.)
            for (int k = 0; k < npvpq; k++)
                triplets.At(k, m, -alpha![pvpq[k]]);

            // Reference-bus P row (row m): off-diagonal H and N entries.
            // ∂P_slack/∂θ_j = V_s·V_j·(G_sj·sin θ_sj − B_sj·cos θ_sj)
            // V_j·∂P_slack/∂V_j = V_s·V_j·(G_sj·cos θ_sj + B_sj·sin θ_sj)
            foreach (var (j, Gsj, Bsj) in ybus.OffDiag[slackIdx])
            {
                int tc = pvpqPos[j]; // θ column index
                int vc = pqPos[j]; // V column index
                if (tc < 0 && vc < 0)
                    continue; // j has no free variable (should not occur)

                double thetaSJ = Va[slackIdx] - Va[j];
                double VVsj = Vm[slackIdx] * Vm[j];
                double csj = Math.Cos(thetaSJ);
                double snj = Math.Sin(thetaSJ);
                double h = VVsj * (Gsj * snj - Bsj * csj); // ∂P_slack/∂θ_j
                double nv = VVsj * (Gsj * csj + Bsj * snj); // V_j · ∂P_slack/∂V_j

                if (tc >= 0)
                    triplets.At(m, tc, h);
                if (vc >= 0)
                    triplets.At(m, npvpq + vc, nv);
            }

            // Corner (m, m): J_pf[m,m] = −∂f_m/∂λ = −α[slackIdx].
            triplets.At(m, m, -alpha![slackIdx]);
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

    private static PowerFlowResult MakeResult(
        PowerNetwork network,
        bool converged,
        int iter,
        double[] Vm,
        double[] Va,
        double mismatch,
        double[] pg,
        double[] qg,
        double lambda = 0
    )
    {
        var flows = BranchFlowCalculator.Compute(network, Vm, Va);
        var vaDeg = Va.Select(a => a * 180.0 / Math.PI).ToArray();

        // Voltage violations are only meaningful when the solver converged.
        // Isolated buses carry no active power; their Vm is whatever the bus data
        // or flat-start set it to and has no physical significance — skip them.
        var violations = new List<VoltageViolation>();
        if (converged)
        {
            for (int i = 0; i < network.Buses.Count; i++)
            {
                var bus = network.Buses[i];
                if (bus.Type == BusType.Isolated)
                    continue;
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
            violations,
            lambda
        );
    }
}
