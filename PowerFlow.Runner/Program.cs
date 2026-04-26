using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

var path = Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "..",
    "PowerFlow.Tests",
    "Data",
    "case14_flatstart.m"
);

var net = MatpowerParser.ParseFile(path);
var result = new NewtonRaphsonSolver().Solve(net);

Console.WriteLine(
    $"Converged: {result.Converged}  Iterations: {result.Iterations}  MaxMismatch: {result.MaxMismatch:e3} pu"
);

Console.WriteLine();
Console.WriteLine($"{"Bus",-4} {"Vm (pu)",-10} {"Va (deg)",-10}");
for (int i = 0; i < net.Buses.Count; i++)
    Console.WriteLine($"{net.Buses[i].Id,-4} {result.Vm[i],-10:F4} {result.Va[i],-10:F3}");

Console.WriteLine();
Console.WriteLine(
    $"{"From",-5} {"To",-5} {"P_ij (pu)",-12} {"Q_ij (pu)",-12} {"P_ji (pu)",-12} {"Q_ji (pu)",-12}"
);
foreach (var bf in result.BranchFlows)
    Console.WriteLine(
        $"{bf.FromBusId,-5} {bf.ToBusId,-5} {bf.Pij,-12:F4} {bf.Qij,-12:F4} {bf.Pji,-12:F4} {bf.Qji,-12:F4}"
    );
