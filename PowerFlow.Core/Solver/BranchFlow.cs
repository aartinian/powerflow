namespace PowerFlow.Core.Solver;

/// <summary>
/// Solved complex power flow at both ends of one in-service branch.
/// Subscript <c>ij</c> is the from-end injection into the branch;
/// <c>ji</c> is the to-end injection. Real power loss on the branch is
/// P_ij + P_ji ≥ 0.
/// All values are in pu on the system base unless noted otherwise.
/// </summary>
public sealed class BranchFlow
{
    /// <summary>Bus ID of the from-end terminal.</summary>
    public int FromBusId { get; }

    /// <summary>Bus ID of the to-end terminal.</summary>
    public int ToBusId { get; }

    /// <summary>Real power injected into the branch at the from-end in pu.</summary>
    public double Pij { get; }

    /// <summary>Reactive power injected into the branch at the from-end in pu.</summary>
    public double Qij { get; }

    /// <summary>Real power injected into the branch at the to-end in pu.</summary>
    public double Pji { get; }

    /// <summary>Reactive power injected into the branch at the to-end in pu.</summary>
    public double Qji { get; }

    /// <summary>Normal thermal rating in pu (0 = unconstrained).</summary>
    public double RateA { get; }

    /// <summary>
    /// Branch loading as a percentage of RateA, based on the higher apparent power end.
    /// Returns <see cref="double.NaN"/> when RateA is zero (unconstrained branch).
    /// </summary>
    public double LoadingPct =>
        RateA > 0
            ? Math.Max(Math.Sqrt(Pij * Pij + Qij * Qij), Math.Sqrt(Pji * Pji + Qji * Qji))
                / RateA
                * 100.0
            : double.NaN;

    /// <summary>Initializes a new branch flow with the specified power injections at both ends and thermal rating.</summary>
    public BranchFlow(
        int fromBusId,
        int toBusId,
        double pij,
        double qij,
        double pji,
        double qji,
        double rateA
    )
    {
        FromBusId = fromBusId;
        ToBusId = toBusId;
        Pij = pij;
        Qij = qij;
        Pji = pji;
        Qji = qji;
        RateA = rateA;
    }
}
