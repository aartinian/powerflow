namespace PowerFlow.Core.Solver;

public class PowerFlowResult
{
    public bool Converged { get; }
    public int Iterations { get; } // number of NR iterations taken
    public double MaxMismatch { get; } // pu — mismatch at the last NR iteration
    public double[] Vm { get; } // pu, indexed by network.Buses order
    public double[] Va { get; } // degrees, indexed by network.Buses order

    public PowerFlowResult(
        bool converged,
        int iterations,
        double maxMismatch,
        double[] vm,
        double[] va
    )
    {
        Converged = converged;
        Iterations = iterations;
        MaxMismatch = maxMismatch;
        Vm = vm;
        Va = va;
    }
}
