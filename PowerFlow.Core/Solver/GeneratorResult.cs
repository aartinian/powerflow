namespace PowerFlow.Core.Solver;

/// <summary>
/// Solved operating point of one in-service generator.
/// Pg includes the distributed-slack correction (α·λ·BaseMVA) allocated
/// proportionally to Pmax; without distributed slack it equals the input dispatch.
/// Qg is the bus-level reactive generation divided equally among all in-service
/// generators at that bus — a documented approximation, since the solver tracks
/// only bus totals, not per-generator reactive allocation.
/// </summary>
public class GeneratorResult
{
    /// <summary>ID of the bus to which this generator is connected.</summary>
    public int BusId { get; }

    /// <summary>
    /// Real-power output in MW. Equals the scheduled dispatch plus any distributed-slack
    /// correction (proportional to Pmax). Identical to the input <c>Pg</c> when
    /// distributed slack is off.
    /// </summary>
    public double Pg { get; }

    /// <summary>
    /// Reactive-power output in MW. The bus-level total reactive generation divided
    /// equally among all in-service generators at the bus.
    /// </summary>
    public double Qg { get; }

    /// <summary>
    /// <c>true</c> when the bus was switched PV→PQ because reactive generation hit
    /// the Qmax ceiling. The bus Vm is no longer regulated in this state.
    /// </summary>
    public bool IsAtQmax { get; }

    /// <summary>
    /// <c>true</c> when the bus was switched PV→PQ because reactive generation hit
    /// the Qmin floor. The bus Vm is no longer regulated in this state.
    /// </summary>
    public bool IsAtQmin { get; }

    /// <summary>Initializes a new generator result with the specified operating point and Q-limit state.</summary>
    public GeneratorResult(int busId, double pg, double qg, bool isAtQmax, bool isAtQmin)
    {
        BusId = busId;
        Pg = pg;
        Qg = qg;
        IsAtQmax = isAtQmax;
        IsAtQmin = isAtQmin;
    }
}
