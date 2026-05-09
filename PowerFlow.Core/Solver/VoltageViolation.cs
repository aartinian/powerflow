namespace PowerFlow.Core.Solver;

/// <summary>A bus whose post-solution voltage magnitude falls outside its declared limits.</summary>
public sealed class VoltageViolation
{
    /// <summary>Bus identifier.</summary>
    public int BusId { get; }

    /// <summary>Actual solved voltage magnitude in pu.</summary>
    public double Vm { get; }

    /// <summary>Lower voltage limit from the bus data in pu.</summary>
    public double Vmin { get; }

    /// <summary>Upper voltage limit from the bus data in pu.</summary>
    public double Vmax { get; }

    /// <summary>Nominal base voltage in kV. Zero when not available from the bus data.</summary>
    public double BaseKv { get; }

    /// <summary><c>true</c> when the solved voltage is below <see cref="Vmin"/>.</summary>
    public bool IsUnderVoltage => Vm < Vmin;

    /// <summary><c>true</c> when the solved voltage is above <see cref="Vmax"/>.</summary>
    public bool IsOverVoltage => Vm > Vmax;

    /// <summary>Solved voltage magnitude in kV (<see cref="Vm"/> × <see cref="BaseKv"/>).</summary>
    public double VmKv => Vm * BaseKv;

    /// <summary>Lower limit in kV (<see cref="Vmin"/> × <see cref="BaseKv"/>).</summary>
    public double VminKv => Vmin * BaseKv;

    /// <summary>Upper limit in kV (<see cref="Vmax"/> × <see cref="BaseKv"/>).</summary>
    public double VmaxKv => Vmax * BaseKv;

    /// <summary>Initializes a new voltage violation record for a bus that exceeded its limits.</summary>
    public VoltageViolation(int busId, double vm, double vmin, double vmax, double baseKv = 0)
    {
        BusId = busId;
        Vm = vm;
        Vmin = vmin;
        Vmax = vmax;
        BaseKv = baseKv;
    }
}
