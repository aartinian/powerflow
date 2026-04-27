namespace PowerFlow.Core.Solver;

public class PowerFlowResult
{
    public bool Converged { get; }
    public int Iterations { get; } // number of NR iterations taken
    public double MaxMismatch { get; } // pu, mismatch at the last NR iteration
    public double[] Vm { get; } // pu, indexed by network.Buses order
    public double[] Va { get; } // degrees, indexed by network.Buses order
    public double[] Pg { get; } // pu, net real generation at each bus (0 for load-only buses)
    public double[] Qg { get; } // pu, net reactive generation at each bus
    public IReadOnlyList<BranchFlow> BranchFlows { get; } // one entry per in-service branch

    public PowerFlowResult(
        bool converged,
        int iterations,
        double maxMismatch,
        double[] vm,
        double[] va,
        double[] pg,
        double[] qg,
        IReadOnlyList<BranchFlow> branchFlows
    )
    {
        Converged = converged;
        Iterations = iterations;
        MaxMismatch = maxMismatch;
        Vm = vm;
        Va = va;
        Pg = pg;
        Qg = qg;
        BranchFlows = branchFlows;
    }
}
