using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

string path;
if (args.Length > 0)
{
    path = args[0];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }
}
else
{
    path = Path.Combine(AppContext.BaseDirectory, "Data", "case14_flatstart.m");
    Console.WriteLine($"No input file specified — running bundled IEEE 14-bus demo.");
    Console.WriteLine($"Usage: PowerFlow.Runner <path-to-case.m>");
    Console.WriteLine();
}

var net = MatpowerParser.ParseFile(path);
var result = new NewtonRaphsonSolver().Solve(net);
double mva = net.BaseMva;

// Clamp values smaller than 0.01 MW/MVAr to zero to suppress floating-point noise in display.
static double D(double v) => Math.Abs(v) < 1e-4 ? 0.0 : v;

Console.WriteLine(
    $"Converged: {result.Converged}  Iterations: {result.Iterations}  MaxMismatch: {result.MaxMismatch:e2} pu"
);

Console.WriteLine();
Console.WriteLine(
    $"{"Bus", -4}  {"Vm (pu)", 8}  {"Va (deg)", 9}  {"Pg (MW)", 8}  {"Qg (MVAr)", 10}  {"Pd (MW)", 8}  {"Qd (MVAr)", 10}"
);
Console.WriteLine(
    $"{"----", -4}  {"--------", 8}  {"---------", 9}  {"--------", 8}  {"----------", 10}  {"--------", 8}  {"----------", 10}"
);
for (int i = 0; i < net.Buses.Count; i++)
    Console.WriteLine(
        $"{net.Buses[i].Id, -4}  {result.Vm[i], 8:F4}  {result.Va[i], 9:F3}  {D(result.Pg[i] * mva), 8:F1}  {D(result.Qg[i] * mva), 10:F1}  {net.Buses[i].Pd, 8:F1}  {net.Buses[i].Qd, 10:F1}"
    );

Console.WriteLine();
Console.WriteLine(
    $"{"From", -4}  {"To", -4}  {"P_ij (MW)", 10}  {"Q_ij (MVAr)", 12}  {"P_ji (MW)", 10}  {"Q_ji (MVAr)", 12}  {"Loss (MW)", 10}  {"Loading", 8}"
);
Console.WriteLine(
    $"{"----", -4}  {"--", -4}  {"----------", 10}  {"------------", 12}  {"----------", 10}  {"------------", 12}  {"----------", 10}  {"--------", 8}"
);
foreach (var bf in result.BranchFlows)
{
    string loading = double.IsNaN(bf.LoadingPct) ? "      - " : $"{bf.LoadingPct, 7:F1}%";
    Console.WriteLine(
        $"{bf.FromBusId, -4}  {bf.ToBusId, -4}  {D(bf.Pij * mva), 10:F2}  {D(bf.Qij * mva), 12:F2}  {D(bf.Pji * mva), 10:F2}  {D(bf.Qji * mva), 12:F2}  {D((bf.Pij + bf.Pji) * mva), 10:F2}  {loading}"
    );
}

if (result.VoltageViolations.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"⚠  Voltage violations ({result.VoltageViolations.Count}):");
    foreach (var v in result.VoltageViolations)
    {
        string kind = v.IsOverVoltage ? "over " : "under";
        Console.WriteLine(
            $"   Bus {v.BusId,-4}  Vm={v.Vm:F4} pu  ({kind}voltage, limit {v.Vmin:F3}–{v.Vmax:F3} pu)"
        );
    }
}

return 0;
