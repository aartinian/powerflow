using CSparse;
using CSparse.Double.Factorization;
using CSparse.Storage;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Solver;

/// <summary>
/// Linearised (DC) power-flow solver. Solves B′θ = P_sched for the
/// non-reference buses in one sparse-LU step, then derives per-branch
/// real-power flows from the resulting angles.
/// <para>
/// Assumptions: V ≈ 1 pu everywhere; sin θ_ij ≈ θ_ij; line resistance
/// is zero (G = 0); shunt elements are ignored. Phase-shifting
/// transformers contribute a constant power-injection term on the
/// right-hand side. Off-nominal tap ratios scale the branch susceptance
/// as b = 1 / (a · X).
/// </para>
/// </summary>
public class DcPowerFlowSolver
{
    /// <summary>
    /// Solve the DC power flow for <paramref name="network"/>. The network
    /// must contain exactly one slack bus. Validate first with
    /// <see cref="NetworkValidator"/> to surface structural issues before
    /// calling.
    /// </summary>
    public DcPowerFlowResult Solve(PowerNetwork network)
    {
        int n = network.Buses.Count;
        int slackIdx = FindSlackIndex(network);
        if (slackIdx < 0)
            throw new InvalidOperationException("Network has no slack bus.");

        // Map bus array index → reduced-system index.
        // Slack bus and isolated buses have no angle equation; they are excluded.
        var busMap = new int[n];
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == slackIdx || network.Buses[i].Type == BusType.Isolated)
                busMap[i] = -1;
            else
                busMap[i] = m++;
        }

        // Scheduled injections (Pg − Pd) / BaseMVA for every bus.
        var Psch = BuildScheduledInjections(network);

        // ── Build reduced B′ matrix and RHS ──────────────────────────────────

        // Each in-service branch contributes ≤ 4 entries to the COO list.
        var bTrips = new CoordinateStorage<double>(m, m, 4 * network.Branches.Count);
        var rhs = new double[m];

        foreach (var br in network.Branches.Where(b => b.IsInService))
        {
            int f = network.IndexOf(br.FromBus);
            int t = network.IndexOf(br.ToBus);
            if (f < 0 || t < 0 || f == t)
                continue;
            if (Math.Abs(br.X) < 1e-10)
                continue; // zero-reactance branch → skip

            double tap = br.TapRatio > 0 ? br.TapRatio : 1.0;
            double b = 1.0 / (tap * br.X);
            double phi = br.PhaseShift * Math.PI / 180.0; // radians

            int fr = busMap[f]; // reduced index for from-bus (-1 = excluded)
            int tr = busMap[t]; // reduced index for to-bus  (-1 = excluded)

            // Diagonal: add b to each active endpoint.
            if (fr >= 0)
                bTrips.At(fr, fr, b);
            if (tr >= 0)
                bTrips.At(tr, tr, b);

            // Off-diagonal: only when both endpoints are active.
            if (fr >= 0 && tr >= 0)
            {
                bTrips.At(fr, tr, -b);
                bTrips.At(tr, fr, -b);
            }

            // Phase-shift power injection: Pφ[f] -= b·φ, Pφ[t] += b·φ.
            if (fr >= 0)
                rhs[fr] -= b * phi;
            if (tr >= 0)
                rhs[tr] += b * phi;
        }

        // Add scheduled injections to RHS for every active (non-slack, non-isolated) bus.
        for (int i = 0; i < n; i++)
            if (busMap[i] >= 0)
                rhs[busMap[i]] += Psch[i];

        // ── Solve B′_red · θ_red = rhs ───────────────────────────────────────

        var theta = new double[n]; // zero-initialised (slack + isolated stay at 0)

        if (m > 0)
        {
            var Bred = CompressedColumnStorage<double>.OfIndexed(bTrips, true);
            var thetaRed = new double[m];
            SparseLU.Create(Bred, ColumnOrdering.MinimumDegreeAtPlusA, 1.0).Solve(rhs, thetaRed);

            for (int i = 0; i < n; i++)
                if (busMap[i] >= 0)
                    theta[i] = thetaRed[busMap[i]];
        }

        // ── Compute branch flows and net injections ───────────────────────────

        var branchFlows = new List<DcBranchFlow>(network.Branches.Count);
        var pinj = new double[n]; // net injected power (pu) from branch flows

        foreach (var br in network.Branches.Where(b => b.IsInService))
        {
            int f = network.IndexOf(br.FromBus);
            int t = network.IndexOf(br.ToBus);
            if (f < 0 || t < 0)
                continue;

            double tap = br.TapRatio > 0 ? br.TapRatio : 1.0;
            double b = Math.Abs(br.X) < 1e-10 ? 0.0 : 1.0 / (tap * br.X);
            double phi = br.PhaseShift * Math.PI / 180.0;
            double P = b * (theta[f] - theta[t] - phi);

            branchFlows.Add(new DcBranchFlow(br.FromBus, br.ToBus, P));
            pinj[f] += P;
            pinj[t] -= P;
        }

        // Net real-power generation at each bus: net injection + load served.
        // For DC (lossless), Σ pinj = 0 exactly, so Σ Pg = total load / BaseMVA.
        var pg = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (network.Buses[i].Type == BusType.Isolated)
                continue;
            pg[i] = pinj[i] + network.Buses[i].Pd / network.BaseMva;
        }

        // Convert angles from radians to degrees.
        var vaDeg = theta.Select(a => a * 180.0 / Math.PI).ToArray();

        return new DcPowerFlowResult(vaDeg, pg, branchFlows);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int FindSlackIndex(PowerNetwork network)
    {
        for (int i = 0; i < network.Buses.Count; i++)
            if (network.Buses[i].Type == BusType.Slack)
                return i;
        return -1;
    }

    private static double[] BuildScheduledInjections(PowerNetwork network)
    {
        int n = network.Buses.Count;
        var Psch = new double[n];
        for (int i = 0; i < n; i++)
            Psch[i] = -network.Buses[i].Pd / network.BaseMva;
        foreach (var gen in network.Generators.Where(g => g.IsInService))
            Psch[network.IndexOf(gen.BusId)] += gen.Pg / network.BaseMva;
        return Psch;
    }
}
