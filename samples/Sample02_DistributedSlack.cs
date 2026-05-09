using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

/// <summary>
/// Sample 2 — Distributed slack (IEEE 14-bus).
///
/// In a conventional solve one "slack bus" absorbs all real-power imbalance.
/// With distributed slack the imbalance is spread across all online generators
/// proportionally to their rated Pmax. This is more realistic for large-scale
/// studies where no single machine should bear all frequency regulation.
///
/// The solver augments the Newton-Raphson system with one extra variable λ (pu)
/// and one extra equation for the total real-power balance. Each generator's
/// scheduled real-power injection shifts by α_i · λ, where α_i is its
/// participation factor (normalised Pmax).
/// </summary>
internal static class Sample02_DistributedSlack
{
    internal static void Run()
    {
        Console.WriteLine("─── Sample 2: Distributed Slack (IEEE 14-bus) ───────────────────");

        var network = MatpowerParser.ParseFile(DataPath("case14.m"));

        // --- Conventional (single slack) solve for comparison ---
        var conventional = new NewtonRaphsonSolver().Solve(network);

        // --- Distributed-slack solve ---
        var distributed = new NewtonRaphsonSolver
        {
            DistributedSlack = true,
        }.Solve(network);

        // Both should converge.
        string ok(bool c) => c ? "✓" : "✗";
        Console.WriteLine($"  Conventional : {ok(conventional.Converged)} " +
                          $"{conventional.Iterations} iter,  λ = {conventional.Lambda:+0.0000;-0.0000} pu");
        Console.WriteLine($"  Distributed  : {ok(distributed.Converged)}  " +
                          $"{distributed.Iterations} iter,  λ = {distributed.Lambda:+0.0000;-0.0000} pu");
        Console.WriteLine();

        // λ is the shared imbalance in pu. Positive means generators collectively
        // produce more than scheduled; negative means under-production.
        double lambdaMw = distributed.Lambda * network.BaseMva;
        Console.WriteLine($"  Shared imbalance λ = {lambdaMw:+0.000;-0.000} MW " +
                          $"({distributed.Lambda:+0.00000;-0.00000} pu on {network.BaseMva} MVA base)");
        Console.WriteLine();

        // Show how each generator's real-power output shifted.
        // GeneratorResult.Pg is already in MW.
        Console.WriteLine("  Generator real-power dispatch:");
        Console.WriteLine("  Bus   Conventional   Distributed   Δ (MW)");
        Console.WriteLine("  ──────────────────────────────────────────");

        for (int i = 0; i < conventional.Generators.Count; i++)
        {
            var gc = conventional.Generators[i];
            var gd = distributed.Generators[i];
            double delta = gd.Pg - gc.Pg;
            string deltaStr = delta >= 0 ? $"+{delta:F3}" : $"{delta:F3}";
            Console.WriteLine($"  {gc.BusId,3}   {gc.Pg,9:F2} MW   " +
                              $"{gd.Pg,9:F2} MW   {deltaStr,8}");
        }

        Console.WriteLine();
    }

    private static string DataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Data", filename);
}
