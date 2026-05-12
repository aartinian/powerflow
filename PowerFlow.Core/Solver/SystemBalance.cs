namespace PowerFlow.Core.Solver;

/// <summary>
/// System-wide power balance quantities computed once after a successful AC solve.
/// All values are in engineering units (MW or MVAr), not per-unit.
/// Only populated when <see cref="PowerFlowResult.Converged"/> is true.
/// </summary>
public sealed class SystemBalance
{
    /// <summary>Total real-power generation dispatched across all in-service generators in MW.</summary>
    public double TotalGenerationMw { get; }

    /// <summary>Total real-power load served across all buses in MW.</summary>
    public double TotalLoadMw { get; }

    /// <summary>
    /// Total real-power losses in MW: Σ (P_ij + P_ji) over all in-service branches.
    /// Always non-negative for a convergent solution.
    /// </summary>
    public double TotalLossesMw { get; }

    /// <summary>Losses as a percentage of total load: TotalLossesMw / TotalLoadMw × 100.</summary>
    public double LossPct { get; }

    /// <summary>Total reactive-power generation across all in-service generators in MVAr.</summary>
    public double TotalGenerationMvar { get; }

    /// <summary>Total reactive-power load served across all buses in MVAr.</summary>
    public double TotalLoadMvar { get; }

    /// <summary>
    /// Net reactive injection from fixed shunt elements in MVAr.
    /// Positive = net capacitive injection (shunts are net sources of reactive power).
    /// </summary>
    public double TotalShuntMvar { get; }

    /// <summary>
    /// Total reactive losses in MVAr: Σ (Q_ij + Q_ji) over all in-service branches.
    /// Can be negative when line charging exceeds inductive losses.
    /// </summary>
    public double TotalLossesMvar { get; }

    /// <summary>Initializes a new system balance summary with the specified totals.</summary>
    public SystemBalance(
        double totalGenerationMw,
        double totalLoadMw,
        double totalLossesMw,
        double lossPct,
        double totalGenerationMvar,
        double totalLoadMvar,
        double totalShuntMvar,
        double totalLossesMvar
    )
    {
        TotalGenerationMw = totalGenerationMw;
        TotalLoadMw = totalLoadMw;
        TotalLossesMw = totalLossesMw;
        LossPct = lossPct;
        TotalGenerationMvar = totalGenerationMvar;
        TotalLoadMvar = totalLoadMvar;
        TotalShuntMvar = totalShuntMvar;
        TotalLossesMvar = totalLossesMvar;
    }
}
