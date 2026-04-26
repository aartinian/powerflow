namespace PowerFlow.Core.Models;

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

    public Generator(
        int busId,
        double pg,
        double qg,
        double qmax,
        double qmin,
        double vg,
        double pmax,
        double pmin,
        bool isInService
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
    }
}
