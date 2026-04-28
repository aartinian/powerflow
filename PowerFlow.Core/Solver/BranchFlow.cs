namespace PowerFlow.Core.Solver;

public class BranchFlow
{
    public int FromBusId { get; }
    public int ToBusId { get; }
    public double Pij { get; } // pu — real power entering branch at from-end
    public double Qij { get; } // pu — reactive power entering branch at from-end
    public double Pji { get; } // pu — real power entering branch at to-end
    public double Qji { get; } // pu — reactive power entering branch at to-end

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
