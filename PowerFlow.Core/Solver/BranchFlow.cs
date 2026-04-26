namespace PowerFlow.Core.Solver;

public class BranchFlow
{
    public int FromBusId { get; }
    public int ToBusId { get; }
    public double Pij { get; } // pu — real power entering branch at from-end
    public double Qij { get; } // pu — reactive power entering branch at from-end
    public double Pji { get; } // pu — real power entering branch at to-end
    public double Qji { get; } // pu — reactive power entering branch at to-end

    public BranchFlow(int fromBusId, int toBusId, double pij, double qij, double pji, double qji)
    {
        FromBusId = fromBusId;
        ToBusId = toBusId;
        Pij = pij;
        Qij = qij;
        Pji = pji;
        Qji = qji;
    }
}
