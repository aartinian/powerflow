namespace PowerFlow.Core.Solver;

/// <summary>
/// The output of <see cref="NewtonRaphsonSolver.Solve(Models.PowerNetwork)"/>. Holds the solved
/// bus state (Vm/Va), per-bus net generation (Pg/Qg), per-branch flows,
/// voltage-limit violations, and — when the solve converged — a system balance
/// summary, per-generator results, and Q-limit binding information.
/// When <see cref="Converged"/> is false the numeric arrays still reflect the
/// last NR iteration but are not a valid solution; <see cref="VoltageViolations"/>
/// and <see cref="Balance"/> are empty/null in that case.
/// </summary>
public class PowerFlowResult
{
    /// <summary><c>true</c> when the Newton-Raphson loop reached the convergence tolerance.</summary>
    public bool Converged { get; }

    /// <summary>
    /// Cumulative Newton-Raphson iterations across all Q-limit outer loops.
    /// Each inner NR pass that converges adds its iteration count here.
    /// </summary>
    public int Iterations { get; }

    /// <summary>
    /// Number of Q-limit outer loop executions. Always ≥ 1 for networks with
    /// non-trivial bus equations; 0 only for single-bus (slack-only) networks.
    /// When <c>EnforceLimits</c> is off this is always 1 (one inner NR pass).
    /// </summary>
    public int OuterIterations { get; }

    /// <summary>
    /// Largest absolute mismatch in pu at the final Newton-Raphson iteration.
    /// Below <c>Tolerance</c> when converged; above it when the solver failed.
    /// </summary>
    public double MaxMismatch { get; }

    /// <summary>Solved voltage magnitudes in pu, one entry per bus in <c>network.Buses</c> order.</summary>
    public double[] Vm { get; }

    /// <summary>Solved voltage angles in degrees, one entry per bus in <c>network.Buses</c> order.</summary>
    public double[] Va { get; }

    /// <summary>
    /// Net real-power generation at each bus in pu, indexed by <c>network.Buses</c> order.
    /// Zero for load-only buses; includes the distributed-slack correction when active.
    /// </summary>
    public double[] Pg { get; }

    /// <summary>
    /// Net reactive-power generation at each bus in pu, indexed by <c>network.Buses</c> order.
    /// Zero for load-only buses.
    /// </summary>
    public double[] Qg { get; }

    /// <summary>Complex power flows at both ends of every in-service branch.</summary>
    public IReadOnlyList<BranchFlow> BranchFlows { get; }

    /// <summary>
    /// Buses whose solved Vm falls outside [Vmin, Vmax]. Empty when the solver did not
    /// converge or when all buses are within limits.
    /// </summary>
    public IReadOnlyList<VoltageViolation> VoltageViolations { get; }

    /// <summary>
    /// System-wide power balance (generation, load, losses). Null when <see cref="Converged"/>
    /// is false.
    /// </summary>
    public SystemBalance? Balance { get; }

    /// <summary>
    /// One entry per in-service generator, in network order. Pg includes any
    /// distributed-slack correction; Qg is divided equally among generators
    /// sharing a bus.
    /// </summary>
    public IReadOnlyList<GeneratorResult> Generators { get; }

    /// <summary>
    /// Buses whose reactive output ended up pinned at a Q-limit.
    /// Key = bus ID; value = <c>true</c> when pinned at Qmax, <c>false</c> when pinned at Qmin.
    /// Empty when Q-limit enforcement is off or no limits were binding.
    /// </summary>
    public IReadOnlyDictionary<int, bool> QLimitBound { get; }

    /// <summary>
    /// Generation imbalance scalar λ in pu (distributed slack only).
    /// The real-power output at participating bus i shifts by α_i · λ · BaseMVA MW
    /// from its scheduled dispatch. Always 0 when distributed slack is off.
    /// </summary>
    public double Lambda { get; }

    /// <summary>Initializes a new result object from a completed or failed Newton-Raphson solve.</summary>
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
        Converged = converged;
        Iterations = iterations;
        OuterIterations = outerIterations;
        MaxMismatch = maxMismatch;
        Vm = vm;
        Va = va;
        Pg = pg;
        Qg = qg;
        BranchFlows = branchFlows;
        VoltageViolations = voltageViolations;
        Lambda = lambda;
        Balance = balance;
        Generators = generators ?? Array.Empty<GeneratorResult>();
        QLimitBound = qLimitBound ?? new Dictionary<int, bool>();
    }
}
