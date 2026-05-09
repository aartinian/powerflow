namespace PowerFlow.Core.Models;

/// <summary>
/// Immutable network data for one bus: type, load, shunt admittance, voltage
/// limits, and an initial Vm/Va that the solver may use as a warm start.
/// The solver owns the mutable voltage state in its own Vm[]/Va[] arrays —
/// this class is pure input data.
/// </summary>
public class Bus
{
    /// <summary>Unique integer bus identifier (matches MATPOWER <c>bus_i</c> column).</summary>
    public int Id { get; }

    /// <summary>Bus role in the power-flow problem: PQ, PV, Slack, or Isolated.</summary>
    public BusType Type { get; }

    /// <summary>Real power demand in MW.</summary>
    public double Pd { get; }

    /// <summary>Reactive power demand in MVAr.</summary>
    public double Qd { get; }

    /// <summary>
    /// Shunt conductance in MW at 1 pu voltage. Divided by BaseMVA before being
    /// added to the Y-bus diagonal as a real shunt load.
    /// </summary>
    public double Gs { get; }

    /// <summary>
    /// Shunt susceptance in MVAr at 1 pu voltage. Divided by BaseMVA before being
    /// added to the Y-bus diagonal. Positive = capacitive (reactive injection).
    /// </summary>
    public double Bs { get; }

    /// <summary>
    /// Initial (or scheduled) voltage magnitude in pu. Used as the solver warm-start
    /// unless <see cref="Solver.NewtonRaphsonSolver.FlatStart"/> overrides it.
    /// </summary>
    public double Vm { get; }

    /// <summary>Initial (or scheduled) voltage angle in degrees.</summary>
    public double Va { get; }

    /// <summary>
    /// Nominal base voltage in kV. Used for kV-scale reporting in
    /// <see cref="Solver.VoltageViolation"/>; does not affect per-unit calculations.
    /// </summary>
    public double BaseKv { get; }

    /// <summary>Maximum acceptable solved voltage magnitude in pu.</summary>
    public double Vmax { get; }

    /// <summary>Minimum acceptable solved voltage magnitude in pu.</summary>
    public double Vmin { get; }

    /// <summary>Initializes a new bus with the specified electrical and geometric parameters.</summary>
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
