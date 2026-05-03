namespace PowerFlow.Core.Solver;

/// <summary>
/// The output of <see cref="NewtonRaphsonSolver.Solve"/>. Holds the solved
/// bus state (Vm/Va), per-bus net generation (Pg/Qg), per-branch flows, and
/// any voltage-limit violations. When <see cref="Converged"/> is false the
/// numeric arrays still reflect the last NR iteration but are not a valid
/// solution; <see cref="VoltageViolations"/> is empty in that case.
/// </summary>
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

    /// <summary>
    /// Buses whose solved Vm falls outside [Vmin, Vmax]. Empty when the solver did not converge
    /// (voltages are meaningless) or when all buses are within limits.
    /// </summary>
    public IReadOnlyList<VoltageViolation> VoltageViolations { get; }

    public PowerFlowResult(
        bool converged,
        int iterations,
        double maxMismatch,
        double[] vm,
        double[] va,
        double[] pg,
        double[] qg,
        IReadOnlyList<BranchFlow> branchFlows,
        IReadOnlyList<VoltageViolation> voltageViolations
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
        VoltageViolations = voltageViolations;
    }
}
