using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

/// <summary>
/// Sample 3 — DC warm-start for AC Newton-Raphson (IEEE 57-bus).
///
/// The Newton-Raphson method converges faster, and is less likely to diverge,
/// when the starting point is already close to the solution. A linearised DC
/// power flow (B′θ = P) gives a cheap, physics-informed guess for the voltage
/// angles. We compare three starting strategies:
///
///   A) Bus-data initialisation — use Vm/Va from the MATPOWER bus array.
///   B) Flat start — Vm = 1.0 pu, Va = 0° for every bus.
///   C) DC warm-start — Vm from bus data (or flat), Va from a single DC solve.
///
/// On well-conditioned networks the difference is small. On larger or
/// more heavily-loaded cases the warm-start often saves 2–4 NR iterations
/// and eliminates divergence risk when the bus data is stale.
/// </summary>
internal static class Sample03_DcWarmStart
{
    internal static void Run()
    {
        Console.WriteLine("─── Sample 3: DC Warm-Start (IEEE 57-bus) ───────────────────────");

        var network = MatpowerParser.ParseFile(DataPath("case57.m"));

        // Strategy A: use voltage profile stored in the case file (warm bus data).
        var resultA = new NewtonRaphsonSolver
        {
            FlatStart    = false,
            WarmStartFromDc = false,
        }.Solve(network);

        // Strategy B: cold flat start — every bus at 1∠0°.
        var resultB = new NewtonRaphsonSolver
        {
            FlatStart    = true,
            WarmStartFromDc = false,
        }.Solve(network);

        // Strategy C: flat Vm but DC-initialised Va before the first NR step.
        var resultC = new NewtonRaphsonSolver
        {
            FlatStart    = true,   // Vm = 1 pu flat; Va will be overwritten by DC
            WarmStartFromDc = true,
        }.Solve(network);

        Console.WriteLine("  Strategy              Converged   Iterations   Max mismatch");
        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        PrintRow("A — bus data",  resultA);
        PrintRow("B — flat start", resultB);
        PrintRow("C — DC warm-start", resultC);
        Console.WriteLine();

        // Verify that all strategies reach the same solution (same bus voltages).
        double maxVmDiff = 0;
        double maxVaDiff = 0;
        for (int i = 0; i < network.Buses.Count; i++)
        {
            maxVmDiff = Math.Max(maxVmDiff, Math.Abs(resultA.Vm[i] - resultC.Vm[i]));
            maxVaDiff = Math.Max(maxVaDiff, Math.Abs(resultA.Va[i] - resultC.Va[i]));
        }
        Console.WriteLine($"  Solution agreement (A vs C): " +
                          $"ΔVm_max = {maxVmDiff:e2} pu,  ΔVa_max = {maxVaDiff:e2}°");
        Console.WriteLine();

        // Stand-alone DC solve — useful when only real-power flows matter.
        // The DC model assumes lossless lines (R = 0), so Pij = –Pji exactly.
        // The most loaded branch gives a quick loading indicator.
        var dcResult = new DcPowerFlowSolver().Solve(network);
        var peak = dcResult.BranchFlows.MaxBy(b => Math.Abs(b.P))!;
        Console.WriteLine($"  DC solve: {dcResult.BranchFlows.Count} branch flows computed. " +
                          $"Peak flow: {peak.P * network.BaseMva:+0.0;-0.0} MW " +
                          $"on branch {peak.FromBus}→{peak.ToBus}");
        Console.WriteLine();
    }

    private static void PrintRow(string label, PowerFlowResult r)
    {
        string conv = r.Converged ? "yes" : "NO";
        Console.WriteLine($"  {label,-22}  {conv,-9}   {r.Iterations,6}       {r.MaxMismatch:e3} pu");
    }

    private static string DataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Data", filename);
}
