namespace PowerFlow.Core.Solver;

/// <summary>A bus whose post-solution voltage magnitude falls outside its declared limits.</summary>
public class VoltageViolation
{
    public int BusId { get; }
    public double Vm { get; }   // pu — actual solved voltage magnitude
    public double Vmin { get; } // pu — lower limit from bus data
    public double Vmax { get; } // pu — upper limit from bus data

    public bool IsUnderVoltage => Vm < Vmin;
    public bool IsOverVoltage => Vm > Vmax;

    public VoltageViolation(int busId, double vm, double vmin, double vmax)
    {
        BusId = busId;
        Vm = vm;
        Vmin = vmin;
        Vmax = vmax;
    }
}
