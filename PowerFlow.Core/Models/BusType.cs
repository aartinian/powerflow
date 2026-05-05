namespace PowerFlow.Core.Models;

/// <summary>
/// Bus role in the power-flow problem. Numeric values match the MATPOWER
/// bus_type column so the parser can cast a parsed integer directly.
/// </summary>
public enum BusType
{
    /// <summary>Load bus: P and Q injection are scheduled, V and θ are unknown.</summary>
    PQ = 1,

    /// <summary>Generator bus: P and |V| are scheduled, Q and θ are unknown.</summary>
    PV = 2,

    /// <summary>Slack/reference bus: |V| and θ are fixed, P and Q absorb the imbalance.</summary>
    Slack = 3,

    /// <summary>Isolated bus: not connected to the network; ignored by the solver.</summary>
    Isolated = 4,
}
