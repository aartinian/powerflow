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
    /// <summary>ID of the bus to which this generator is connected.</summary>
    public int BusId { get; }

    /// <summary>Scheduled real power output in MW.</summary>
    public double Pg { get; }

    /// <summary>
    /// Reactive power output in MVAr. Used as an initial value; the solver
    /// overwrites this at PV and slack buses to satisfy the power-flow equations.
    /// </summary>
    public double Qg { get; }

    /// <summary>
    /// Upper reactive capability limit in MVAr. When <c>EnforceLimits</c> is on,
    /// the bus is switched PV→PQ and Qg is pinned here if this ceiling is hit.
    /// </summary>
    public double Qmax { get; }

    /// <summary>
    /// Lower reactive capability limit in MVAr. When <c>EnforceLimits</c> is on,
    /// the bus is switched PV→PQ and Qg is pinned here if this floor is hit.
    /// </summary>
    public double Qmin { get; }

    /// <summary>
    /// Voltage magnitude setpoint in pu. Enforced at PV and slack buses as the
    /// regulated terminal voltage. When multiple generators share a PV bus with
    /// differing setpoints, the last generator's value wins (validator warns).
    /// </summary>
    public double Vg { get; }

    /// <summary>Maximum real power capability in MW. Used as the participation-factor weight in distributed slack.</summary>
    public double Pmax { get; }

    /// <summary>Minimum stable real power output in MW.</summary>
    public double Pmin { get; }

    /// <summary><c>true</c> when this generator contributes to the power-flow equations.</summary>
    public bool IsInService { get; }

    /// <summary>
    /// Machine MVA base. 0 means the system <c>BaseMVA</c> is used (the default
    /// when the MATPOWER <c>mBase</c> column is absent or zero).
    /// </summary>
    public double MBase { get; }

    /// <summary>Initializes a new generator with the specified dispatch and capability data.</summary>
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
