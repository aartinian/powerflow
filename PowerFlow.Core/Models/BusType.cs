namespace PowerFlow.Core.Models;

// Values match MATPOWER bus_type column so the parser can cast directly.
public enum BusType
{
    PQ = 1,
    PV = 2,
    Slack = 3,
    Isolated = 4,
}
