namespace PowerFlow.Core.Solver;

/// <summary>
/// System-wide power balance quantities computed once after a successful AC solve.
/// All values are in engineering units (MW or MVAr), not per-unit.
/// Only populated when <see cref="PowerFlowResult.Converged"/> is true.
/// </summary>
public class SystemBalance
{
    public double TotalGenerationMw  { get; } // MW
    public double TotalLoadMw        { get; } // MW
    public double TotalLossesMw      { get; } // MW   — Σ (Pij + Pji) over all in-service branches
    public double LossPct            { get; } // %    — TotalLossesMw / TotalLoadMw × 100
    public double TotalGenerationMvar { get; } // MVAr
    public double TotalLoadMvar       { get; } // MVAr
    public double TotalShuntMvar     { get; } // MVAr — net reactive injection from shunts (positive = capacitive)
    public double TotalLossesMvar    { get; } // MVAr — Σ (Qij + Qji) over all in-service branches

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
        TotalGenerationMw   = totalGenerationMw;
        TotalLoadMw         = totalLoadMw;
        TotalLossesMw       = totalLossesMw;
        LossPct             = lossPct;
        TotalGenerationMvar = totalGenerationMvar;
        TotalLoadMvar       = totalLoadMvar;
        TotalShuntMvar      = totalShuntMvar;
        TotalLossesMvar     = totalLossesMvar;
    }
}
