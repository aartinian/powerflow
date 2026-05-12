namespace PowerFlow.Core.Solver;

/// <summary>
/// Result of a DC (linearised) power-flow solve. Contains per-bus voltage
/// angles and net real-power generation, plus per-branch real-power flows.
/// Voltage magnitudes are implicitly 1 pu throughout; reactive power and
/// voltage violations are undefined in the DC model.
/// </summary>
public sealed class DcPowerFlowResult
{
    /// <summary>
    /// Bus voltage angles in degrees, indexed by network.Buses order.
    /// The slack (reference) bus always has Va = 0; isolated buses also
    /// carry 0 (their angle is undetermined by the DC equations).
    /// </summary>
    public double[] Va { get; }

    /// <summary>
    /// Net real-power generation at each bus in pu, indexed by
    /// network.Buses order. Equals ΣPg/BaseMVA for buses with in-service
    /// generators; equals the dispatch required to close the DC power
    /// balance at the slack bus. Zero for isolated buses.
    /// </summary>
    public double[] Pg { get; }

    /// <summary>
    /// Real-power flow on each in-service branch, in network order.
    /// Positive = power flows from-bus → to-bus; negative = to-bus →
    /// from-bus.
    /// </summary>
    public IReadOnlyList<DcBranchFlow> BranchFlows { get; }

    /// <summary>Initializes a new DC power-flow result with the specified angles, generation, and branch flows.</summary>
    public DcPowerFlowResult(double[] va, double[] pg, IReadOnlyList<DcBranchFlow> branchFlows)
    {
        Va = va;
        Pg = pg;
        BranchFlows = branchFlows;
    }
}

/// <summary>
/// Real-power flow (pu) on one DC branch.
/// Positive means power flows from <see cref="FromBus"/> to <see cref="ToBus"/>.
/// </summary>
public sealed class DcBranchFlow
{
    /// <summary>Bus ID of the from-end (sending) terminal.</summary>
    public int FromBus { get; }

    /// <summary>Bus ID of the to-end (receiving) terminal.</summary>
    public int ToBus { get; }

    /// <summary>
    /// Real-power flow on the branch in pu on the system base.
    /// Positive = power flows from the from-end to the to-end; negative = reverse flow.
    /// </summary>
    public double P { get; }

    /// <summary>Initializes a new DC branch flow with the specified terminals and real-power injection.</summary>
    public DcBranchFlow(int fromBus, int toBus, double p)
    {
        FromBus = fromBus;
        ToBus = toBus;
        P = p;
    }
}
