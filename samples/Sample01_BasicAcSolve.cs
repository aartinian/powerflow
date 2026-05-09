using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

/// <summary>
/// Sample 1 — Basic AC power-flow solve (IEEE 14-bus).
///
/// Demonstrates the minimal API: parse a MATPOWER case file, run the
/// Newton-Raphson solver with default settings, then print per-bus
/// voltage magnitude and angle.
/// </summary>
internal static class Sample01_BasicAcSolve
{
    internal static void Run()
    {
        Console.WriteLine("─── Sample 1: Basic AC Solve (IEEE 14-bus) ──────────────────────");

        // 1. Load a MATPOWER case file.
        //    ParseFile accepts a path to any *.m case file in MATPOWER format.
        var network = MatpowerParser.ParseFile(DataPath("case14.m"));

        // 2. Create and configure the solver.
        //    All settings are init-only properties; they can be overridden here.
        var solver = new NewtonRaphsonSolver
        {
            Tolerance    = 1e-6,   // convergence criterion (pu) — MATPOWER default
            MaxIterations = 50,    // upper bound on NR iterations
            EnforceLimits = true,  // enforce generator Q-limits (PV→PQ switching)
            FlatStart    = false,  // use voltage magnitudes/angles from the bus data
        };

        // 3. Solve.
        var result = solver.Solve(network);

        // 4. Check convergence.
        if (!result.Converged)
        {
            Console.WriteLine($"  ✗ Did not converge (max mismatch {result.MaxMismatch:e3} pu)");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  ✓ Converged in {result.Iterations} iterations " +
                          $"(max mismatch {result.MaxMismatch:e3} pu)");
        Console.WriteLine();

        // 5. Print per-bus voltages.
        Console.WriteLine("  Bus   Vm (pu)   Va (°)");
        Console.WriteLine("  ────────────────────────");
        for (int i = 0; i < network.Buses.Count; i++)
        {
            Console.WriteLine($"  {network.Buses[i].Id,3}   {result.Vm[i]:F4}    {result.Va[i]:+0.00;-0.00}");
        }

        // 6. Print any voltage violations.
        if (result.VoltageViolations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Voltage violations:");
            foreach (var v in result.VoltageViolations)
            {
                string tag = v.IsUnderVoltage ? "UNDER" : "OVER";
                Console.WriteLine($"    Bus {v.BusId}: {tag} — Vm = {v.Vm:F4} pu " +
                                  $"(limits [{v.Vmin:F3}, {v.Vmax:F3}])");
            }
        }

        Console.WriteLine();
    }

    private static string DataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Data", filename);
}
