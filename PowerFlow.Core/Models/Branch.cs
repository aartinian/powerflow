namespace PowerFlow.Core.Models;

/// <summary>
/// Immutable network data for one transmission line or transformer using the
/// off-nominal-tap π model. R, X, B are in pu on the network base; ratings
/// are in MVA; PhaseShift, Angmin, and Angmax are in degrees.
/// <see cref="TapRatio"/> is normalised to 1.0 when the input is 0 (MATPOWER
/// convention for plain lines). <see cref="Angmin"/>/<see cref="Angmax"/>
/// default to ±360° (unconstrained) when absent from the case file.
/// </summary>
public class Branch
{
    /// <summary>Bus ID of the from-end (sending) terminal.</summary>
    public int FromBus { get; }

    /// <summary>Bus ID of the to-end (receiving) terminal.</summary>
    public int ToBus { get; }

    /// <summary>Series resistance in pu on the system base.</summary>
    public double R { get; }

    /// <summary>Series reactance in pu on the system base.</summary>
    public double X { get; }

    /// <summary>
    /// Total line charging susceptance in pu on the system base.
    /// Split equally (B/2) at each end of the π equivalent circuit.
    /// </summary>
    public double B { get; }

    /// <summary>
    /// Off-nominal turns ratio (transformer tap magnitude). A value of 0 in the
    /// source data is normalised to 1.0 by the constructor (MATPOWER convention
    /// for untapped lines). Always ≥ 1.0 after construction.
    /// </summary>
    public double TapRatio { get; }

    /// <summary>Transformer phase shift in degrees (positive = leading).</summary>
    public double PhaseShift { get; }

    /// <summary>Normal continuous thermal rating in MVA. 0 = unconstrained.</summary>
    public double RateA { get; }

    /// <summary>Short-term (short-circuit) thermal rating in MVA. 0 = unconstrained.</summary>
    public double RateB { get; }

    /// <summary>Emergency thermal rating in MVA. 0 = unconstrained.</summary>
    public double RateC { get; }

    /// <summary>
    /// Minimum angle difference θ_f − θ_t in degrees. −360 = unconstrained.
    /// Stored for reference; not enforced by the solver (see known limitations).
    /// </summary>
    public double Angmin { get; }

    /// <summary>
    /// Maximum angle difference θ_f − θ_t in degrees. 360 = unconstrained.
    /// Stored for reference; not enforced by the solver (see known limitations).
    /// </summary>
    public double Angmax { get; }

    /// <summary><c>true</c> when the branch participates in the Y-bus and power-flow equations.</summary>
    public bool IsInService { get; }

    /// <summary>Initializes a new branch with the specified parameters. Tap ratio is normalised to 1.0 if zero.</summary>
    public Branch(
        int fromBus,
        int toBus,
        double r,
        double x,
        double b,
        double tapRatio,
        double phaseShift,
        double rateA,
        bool isInService,
        double rateB = 0,
        double rateC = 0,
        double angmin = -360,
        double angmax = 360
    )
    {
        FromBus = fromBus;
        ToBus = toBus;
        R = r;
        X = x;
        B = b;
        // MATPOWER uses 0 to mean "no transformer"; normalise here so callers always get a real ratio.
        TapRatio = tapRatio == 0.0 ? 1.0 : tapRatio;
        PhaseShift = phaseShift;
        RateA = rateA;
        RateB = rateB;
        RateC = rateC;
        Angmin = angmin;
        Angmax = angmax;
        IsInService = isInService;
    }
}
