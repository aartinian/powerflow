namespace PowerFlow.Core.Models;

/// <summary>
/// Immutable dispatch and capability data for one generator: real-power
/// schedule, reactive limits, voltage setpoint, and active-power capacity.
/// Multiple generators may share a bus; the solver aggregates their
/// contributions per bus. <see cref="MBase"/> is the machine MVA base;
/// 0 means "use the system base" (the standard when the field is absent).
/// </summary>
public class Generator
{
    public int BusId { get; }
    public double Pg { get; } // MW   scheduled real power output
    public double Qg { get; } // MVAr reactive power output (initial; solver result for PV/slack)
    public double Qmax { get; } // MVAr upper reactive limit — used for PV→PQ switching
    public double Qmin { get; } // MVAr lower reactive limit — used for PV→PQ switching
    public double Vg { get; } // pu   voltage setpoint — enforced at PV and slack buses
    public double Pmax { get; } // MW
    public double Pmin { get; } // MW
    public bool IsInService { get; }
    public double MBase { get; } // MVA machine base; 0 = use system base

    public Generator(
        int busId,
        double pg,
        double qg,
        double qmax,
        double qmin,
        double vg,
        double pmax,
        double pmin,
        bool isInService,
        double mBase = 0
    )
    {
        BusId = busId;
        Pg = pg;
        Qg = qg;
        Qmax = qmax;
        Qmin = qmin;
        Vg = vg;
        Pmax = pmax;
        Pmin = pmin;
        IsInService = isInService;
        MBase = mBase;
    }
}
