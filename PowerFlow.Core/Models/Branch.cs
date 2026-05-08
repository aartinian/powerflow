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
    public int FromBus { get; }
    public int ToBus { get; }
    public double R { get; } // pu resistance
    public double X { get; } // pu reactance
    public double B { get; } // pu total line charging susceptance (split equally at each end in π model)
    public double TapRatio { get; } // off-nominal turns ratio
    public double PhaseShift { get; } // deg transformer phase shift
    public double RateA { get; } // MVA normal thermal rating (0 = unconstrained)
    public double RateB { get; } // MVA short-circuit rating (0 = unconstrained)
    public double RateC { get; } // MVA emergency rating (0 = unconstrained)
    public double Angmin { get; } // deg minimum angle difference (Θf − Θt); −360 = unconstrained
    public double Angmax { get; } // deg maximum angle difference (Θf − Θt);  360 = unconstrained
    public bool IsInService { get; }

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
