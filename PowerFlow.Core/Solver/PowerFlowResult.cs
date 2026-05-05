namespace PowerFlow.Core.Solver;

/// <summary>
/// The output of <see cref="NewtonRaphsonSolver.Solve"/>. Holds the solved
/// bus state (Vm/Va), per-bus net generation (Pg/Qg), per-branch flows,
/// voltage-limit violations, and — when the solve converged — a system balance
/// summary, per-generator results, and Q-limit binding information.
/// When <see cref="Converged"/> is false the numeric arrays still reflect the
/// last NR iteration but are not a valid solution; <see cref="VoltageViolations"/>
/// and <see cref="Balance"/> are empty/null in that case.
/// </summary>
public class PowerFlowResult
{
    public bool Converged      { get; }
    public int Iterations      { get; } // cumulative NR iterations across all outer Q-limit loops
    public int OuterIterations { get; } // Q-limit outer loop count (0 when limits are off)
    public double MaxMismatch  { get; } // pu, mismatch at the last NR iteration
    public double[] Vm { get; }  // pu, indexed by network.Buses order
    public double[] Va { get; }  // degrees, indexed by network.Buses order
    public double[] Pg { get; }  // pu, net real generation at each bus (0 for load-only buses)
    public double[] Qg { get; }  // pu, net reactive generation at each bus
    public IReadOnlyList<BranchFlow> BranchFlows { get; }

    /// <summary>
    /// Buses whose solved Vm falls outside [Vmin, Vmax]. Empty when the solver did not
    /// converge or when all buses are within limits.
    /// </summary>
    public IReadOnlyList<VoltageViolation> VoltageViolations { get; }

    /// <summary>
    /// System-wide power balance. Null when <see cref="Converged"/> is false.
    /// </summary>
    public SystemBalance? Balance { get; }

    /// <summary>
    /// One entry per in-service generator, in network order.
    /// </summary>
    public IReadOnlyList<GeneratorResult> Generators { get; }

    /// <summary>
    /// Buses whose reactive output ended up pinned at a Q-limit.
    /// Key = bus ID; value = true when pinned at Qmax, false when pinned at Qmin.
    /// Empty when Q-limit enforcement is off or no limits were binding.
    /// </summary>
    public IReadOnlyDictionary<int, bool> QLimitBound { get; }

    /// <summary>
    /// Generation imbalance scalar λ in pu (distributed slack only).
    /// The real power at each participating bus i shifts by α_i · λ · BaseMVA MW
    /// from the scheduled dispatch. Always 0 when distributed slack is off.
    /// </summary>
    public double Lambda { get; }

    public PowerFlowResult(
        bool converged,
        int iterations,
        int outerIterations,
        double maxMismatch,
        double[] vm,
        double[] va,
        double[] pg,
        double[] qg,
        IReadOnlyList<BranchFlow> branchFlows,
        IReadOnlyList<VoltageViolation> voltageViolations,
        double lambda = 0,
        SystemBalance? balance = null,
        IReadOnlyList<GeneratorResult>? generators = null,
        IReadOnlyDictionary<int, bool>? qLimitBound = null
    )
    {
        Converged         = converged;
        Iterations        = iterations;
        OuterIterations   = outerIterations;
        MaxMismatch       = maxMismatch;
        Vm                = vm;
        Va                = va;
        Pg                = pg;
        Qg                = qg;
        BranchFlows       = branchFlows;
        VoltageViolations = voltageViolations;
        Lambda            = lambda;
        Balance           = balance;
        Generators        = generators  ?? Array.Empty<GeneratorResult>();
        QLimitBound       = qLimitBound ?? new Dictionary<int, bool>();
    }
}
