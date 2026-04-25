namespace PowerFlow.Core.Models;

// Immutable network data only. The solver owns mutable voltage state in its own Vm[]/Va[] arrays.
public class Bus
{
    public int Id { get; }
    public BusType Type { get; }
    public double Pd { get; } // MW   load demand
    public double Qd { get; } // MVAr load demand
    public double Gs { get; } // MW   — divide by baseMVA when building Y-bus
    public double Bs { get; } // MVAr — divide by baseMVA when building Y-bus
    public double Vm { get; } // pu   initial voltage magnitude (used as solver flat-start)
    public double Va { get; } // deg  initial voltage angle
    public double BaseKv { get; } // kV   base voltage — per-unit reference, used for reporting only
    public double Vmax { get; } // pu
    public double Vmin { get; } // pu

    public Bus(
        int id,
        BusType type,
        double pd,
        double qd,
        double gs,
        double bs,
        double vm,
        double va,
        double baseKv,
        double vmax,
        double vmin
    )
    {
        Id = id;
        Type = type;
        Pd = pd;
        Qd = qd;
        Gs = gs;
        Bs = bs;
        Vm = vm;
        Va = va;
        BaseKv = baseKv;
        Vmax = vmax;
        Vmin = vmin;
    }
}
