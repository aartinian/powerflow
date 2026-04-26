using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

// Start from the MATPOWER solution stored in case14.m.
// If the network data is correct, the initial mismatch is already < 1e-6 and the solver returns in 0 steps.
var path = Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "..",
    "PowerFlow.Tests",
    "Data",
    "case14.m"
);
var net = MatpowerParser.ParseFile(path);
var result = new NewtonRaphsonSolver().Solve(net);

Console.WriteLine(
    $"Converged: {result.Converged}  Iterations: {result.Iterations}  MaxMismatch: {result.MaxMismatch:e6}"
);
Console.WriteLine();
Console.WriteLine(
    $"{"Bus", -4} {"Vm solver", -12} {"Vm case14", -12} {"Va solver", -12} {"Va case14", -12}"
);
for (int i = 0; i < net.Buses.Count; i++)
    Console.WriteLine(
        $"{net.Buses[i].Id, -4} {result.Vm[i], -12:F6} {net.Buses[i].Vm, -12:F6} {result.Va[i], -12:F4} {net.Buses[i].Va, -12:F4}"
    );
