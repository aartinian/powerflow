namespace PowerFlow.Core.Models;

public class Branch
{
    public int FromBus { get; }
    public int ToBus { get; }
    public double R { get; } // pu resistance
    public double X { get; } // pu reactance
    public double B { get; } // pu total line charging susceptance (split equally at each end in π model)
    public double TapRatio { get; } // off-nominal turns ratio
    public double PhaseShift { get; } // deg transformer phase shift
    public bool IsInService { get; }

    public Branch(
        int fromBus,
        int toBus,
        double r,
        double x,
        double b,
        double tapRatio,
        double phaseShift,
        bool isInService
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
        IsInService = isInService;
    }
}
