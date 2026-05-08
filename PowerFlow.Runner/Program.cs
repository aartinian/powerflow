using System.Globalization;
using Microsoft.Extensions.Logging;
using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;
using PowerFlow.Core.Validation;

// ── Argument parsing ──────────────────────────────────────────────────────────

string? path = null;
bool flatStart = false;
bool distSlack = false;
bool dcMode = false;
bool noLimits = false;
bool noColor = false;
bool noBuses = false;
bool noBranches = false;
double tol = 1e-6;
int maxIter = 50;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--flat-start":
            flatStart = true;
            break;
        case "--distributed-slack":
            distSlack = true;
            break;
        case "--dc":
            dcMode = true;
            break;
        case "--no-limits":
            noLimits = true;
            break;
        case "--no-color":
            noColor = true;
            break;
        case "--no-buses":
            noBuses = true;
            break;
        case "--no-branches":
            noBranches = true;
            break;
        case "--summary-only":
            noBuses = true;
            noBranches = true;
            break;
        case "--tol":
            if (++i < args.Length &&
                !double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out tol))
            {
                Console.Error.WriteLine($"Warning: invalid --tol value '{args[i]}', using default 1e-6.");
                tol = 1e-6;
            }
            break;
        case "--max-iter":
            if (++i < args.Length)
            {
                if (!int.TryParse(args[i], out maxIter) || maxIter <= 0)
                {
                    Console.Error.WriteLine($"Warning: invalid --max-iter value '{args[i]}', using default 50.");
                    maxIter = 50;
                }
            }
            break;
        default:
            if (!args[i].StartsWith("--"))
                path = args[i];
            break;
    }
}

// ── File resolution ───────────────────────────────────────────────────────────

if (path is null)
{
    path = Path.Combine(AppContext.BaseDirectory, "Data", "case14.m");
    flatStart = true;
    Console.WriteLine("No input file specified — running bundled IEEE 14-bus demo (flat start).");
    Console.WriteLine("Usage: PowerFlow.Runner [options] <path-to-case.m>");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --flat-start          force Vm=1 pu, Va=0° initial guess");
    Console.WriteLine("  --distributed-slack   spread imbalance by Pmax participation");
    Console.WriteLine("  --dc                  linearised DC power flow (single LU step)");
    Console.WriteLine("  --no-limits           disable Q-limit enforcement (PV→PQ switching)");
    Console.WriteLine("  --tol <ε>             convergence tolerance  (default 1e-6)");
    Console.WriteLine("  --max-iter <n>        NR iteration cap       (default 50)");
    Console.WriteLine("  --no-color            disable ANSI colour output");
    Console.WriteLine("  --no-buses            suppress the bus results table");
    Console.WriteLine("  --no-branches         suppress the branch results table");
    Console.WriteLine("  --summary-only        suppress both tables (equivalent to both above)");
    Console.WriteLine();
}
else if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

// ── Colour helpers ────────────────────────────────────────────────────────────

bool color = !noColor && !Console.IsOutputRedirected;
string Esc(string c) => color ? $"\x1b[{c}m" : "";
string R() => Esc("0"); // reset
string B() => Esc("1"); // bold
string BG() => Esc("1;32"); // bold green
string BR() => Esc("1;31"); // bold red
string BY() => Esc("1;33"); // bold yellow

// ── Parse ─────────────────────────────────────────────────────────────────────

var net = MatpowerParser.ParseFile(path!);
double mva = net.BaseMva;
int nBus = net.Buses.Count;
int nBrIn = net.Branches.Count(b => b.IsInService);
int nBrOff = net.Branches.Count(b => !b.IsInService);
int nGen = net.Generators.Count(g => g.IsInService);
string caseName = Path.GetFileNameWithoutExtension(path!);

Console.WriteLine(
    $"{B()}{caseName}{R()}  │  {nBus} bus{(nBus != 1 ? "es" : "")}  "
        + $"{nBrIn} branch{(nBrIn != 1 ? "es" : "")}  "
        + $"{nGen} generator{(nGen != 1 ? "s" : "")}  {mva:F0} MVA base"
        + (nBrOff > 0 ? $"  ({nBrOff} out-of-service)" : "")
);
Console.WriteLine();

// ── Validate ──────────────────────────────────────────────────────────────────

var validation = NetworkValidator.Validate(net);
if (validation.Errors.Count > 0)
{
    foreach (var issue in validation.Errors)
    {
        bool isErr = issue.Severity == ValidationSeverity.Error;
        string pfx = isErr ? $"{BR()}ERROR{R()}" : $"{BY()}WARN {R()}";
        Console.WriteLine($"  {pfx}  [{issue.Code}] {issue.Message}");
    }
    if (!validation.IsValid)
    {
        Console.Error.WriteLine("\nAborting: correct the above errors before solving.");
        return 1;
    }
    Console.WriteLine();
}

// ── Suppress floating-point noise in display ──────────────────────────────────

static double D(double v) => Math.Abs(v) < 1e-4 ? 0.0 : v;

// ── DC path ───────────────────────────────────────────────────────────────────

if (dcMode)
{
    var dc = new DcPowerFlowSolver().Solve(net);

    Console.WriteLine($"  {BG()}DC POWER FLOW — SOLVED{R()}");
    Console.WriteLine();

    // Bus table
    if (!noBuses)
    {
        Console.WriteLine($"{"Bus", -4}  {"Va (deg)", 9}  {"Pg (MW)", 8}  {"Pd (MW)", 8}");
        Console.WriteLine($"{"----", -4}  {"---------", 9}  {"--------", 8}  {"--------", 8}");
        for (int i = 0; i < nBus; i++)
            Console.WriteLine(
                $"{net.Buses[i].Id, -4}  {dc.Va[i], 9:F3}  {D(dc.Pg[i] * mva), 8:F1}  {net.Buses[i].Pd, 8:F1}"
            );
        Console.WriteLine();
    }

    // Branch table — always iterate (needed for thermal violation detection)
    var inSvcBranches = net.Branches.Where(b => b.IsInService).ToList();
    var thermalViolsDc = new List<(int From, int To, double Pct, double RateA)>();

    if (!noBranches)
    {
        Console.WriteLine($"{"From", -4}  {"To", -4}  {"P_ij (MW)", 10}  {"Loading", 8}");
        Console.WriteLine($"{"----", -4}  {"--", -4}  {"----------", 10}  {"--------", 8}");
    }
    for (int i = 0; i < dc.BranchFlows.Count; i++)
    {
        var bf = dc.BranchFlows[i];
        double rateA = i < inSvcBranches.Count ? inSvcBranches[i].RateA : 0.0;
        double? loadPct = rateA > 0 ? Math.Abs(bf.P) * mva / rateA * 100.0 : null;
        bool over = loadPct.HasValue && loadPct.Value > 100.0;

        if (over)
            thermalViolsDc.Add((bf.FromBus, bf.ToBus, loadPct!.Value, rateA));

        if (!noBranches)
        {
            string loading = loadPct.HasValue ? $"{loadPct.Value, 7:F1}%" : "      - ";
            if (over)
                loading = $"{BY()}{loading}{R()}";
            Console.WriteLine(
                $"{bf.FromBus, -4}  {bf.ToBus, -4}  {D(bf.P * mva), 10:F2}  {loading}"
            );
        }
    }

    // Thermal violations
    if (thermalViolsDc.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"{BY()}!!! Thermal violations ({thermalViolsDc.Count}):{R()}");
        foreach (var (from, to, pct, rA) in thermalViolsDc)
            Console.WriteLine(
                $"   Branch {from, -4}→{to, -4}  Loading={pct:F1}%  (limit {rA * mva:F1} MVA)"
            );
    }

    // Summary footer
    double pgDc = dc.Pg.Sum() * mva;
    double pdDc = net.Buses.Sum(b => b.Pd);
    Console.WriteLine();
    Console.WriteLine($"  Total Pg   {pgDc, 8:F1} MW     Total Pd   {pdDc, 8:F1} MW");
    Console.WriteLine("  Losses     0.0 MW  (lossless DC model)");

    return 0;
}

// ── AC Newton-Raphson path ────────────────────────────────────────────────────

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information)
);
var log = loggerFactory.CreateLogger("PowerFlow");

var result = new NewtonRaphsonSolver
{
    Log = log,
    FlatStart = flatStart,
    DistributedSlack = distSlack,
    EnforceLimits = !noLimits,
    Tolerance = tol,
    MaxIterations = maxIter,
}.Solve(net);

Console.WriteLine();

// Convergence banner
string iters = $"{result.Iterations} iteration{(result.Iterations != 1 ? "s" : "")}";
if (result.Converged)
    Console.WriteLine(
        $"  {BG()}CONVERGED{R()} in {iters}  max |mismatch| = {result.MaxMismatch:E3} pu"
    );
else
    Console.WriteLine(
        $"  {BR()}DID NOT CONVERGE{R()} after {iters}  max |mismatch| = {result.MaxMismatch:E3} pu"
    );
Console.WriteLine();

// Ruler helper (local function — callable anywhere in this scope)
string Ruler(string label) =>
    $"{BY()}── {label} {new string('─', Math.Max(0, 44 - label.Length))}─{R()}";

// Bus table
if (!noBuses)
{
    Console.WriteLine(
        $"{"Bus", -4}  {"Vm (pu)", 8}  {"Va (deg)", 9}  {"Pg (MW)", 8}  {"Qg (MVAr)", 10}  {"Pd (MW)", 8}  {"Qd (MVAr)", 10}"
    );
    Console.WriteLine(
        $"{"----", -4}  {"--------", 8}  {"---------", 9}  {"--------", 8}  {"----------", 10}  {"--------", 8}  {"----------", 10}"
    );
    for (int i = 0; i < nBus; i++)
        Console.WriteLine(
            $"{net.Buses[i].Id, -4}  {result.Vm[i], 8:F4}  {result.Va[i], 9:F3}  "
                + $"{D(result.Pg[i] * mva), 8:F1}  {D(result.Qg[i] * mva), 10:F1}  "
                + $"{net.Buses[i].Pd, 8:F1}  {net.Buses[i].Qd, 10:F1}"
        );
    Console.WriteLine();

    // Generator table
    if (result.Generators.Count > 0)
    {
        Console.WriteLine($"{"Bus", -4}  {"Pg (MW)", 8}  {"Qg (MVAr)", 10}  {"Q-limit", 7}");
        Console.WriteLine($"{"----", -4}  {"--------", 8}  {"----------", 10}  {"-------", 7}");
        foreach (var gr in result.Generators)
        {
            string qlim =
                gr.IsAtQmax ? $"{BY()}Qmax{R()}"
                : gr.IsAtQmin ? $"{BY()}Qmin{R()}"
                : "";
            Console.WriteLine($"{gr.BusId, -4}  {gr.Pg, 8:F1}  {gr.Qg, 10:F1}  {qlim}");
        }
        Console.WriteLine();
    }
}

// Branch table — always iterate (needed for thermal violation detection)
var thermalViols = new List<BranchFlow>();

if (!noBranches)
{
    Console.WriteLine(
        $"{"From", -4}  {"To", -4}  {"P_ij (MW)", 10}  {"Q_ij (MVAr)", 12}  "
            + $"{"P_ji (MW)", 10}  {"Q_ji (MVAr)", 12}  {"Loss (MW)", 10}  {"Loading", 8}"
    );
    Console.WriteLine(
        $"{"----", -4}  {"--", -4}  {"----------", 10}  {"------------", 12}  "
            + $"{"----------", 10}  {"------------", 12}  {"----------", 10}  {"--------", 8}"
    );
}
foreach (var bf in result.BranchFlows)
{
    bool over = !double.IsNaN(bf.LoadingPct) && bf.LoadingPct > 100.0;
    if (over)
        thermalViols.Add(bf);

    if (!noBranches)
    {
        string loading = double.IsNaN(bf.LoadingPct) ? "      - " : $"{bf.LoadingPct, 7:F1}%";
        if (over)
            loading = $"{BY()}{loading}{R()}";
        Console.WriteLine(
            $"{bf.FromBusId, -4}  {bf.ToBusId, -4}  {D(bf.Pij * mva), 10:F2}  {D(bf.Qij * mva), 12:F2}  "
                + $"{D(bf.Pji * mva), 10:F2}  {D(bf.Qji * mva), 12:F2}  {D((bf.Pij + bf.Pji) * mva), 10:F2}  {loading}"
        );
    }
}

// Voltage violations
if (result.VoltageViolations.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(Ruler("Voltage Violations"));
    foreach (var v in result.VoltageViolations)
    {
        string kind = v.IsOverVoltage ? "over " : "under";
        string kvPart =
            v.BaseKv > 0 ? $"  ({v.VmKv:F2} kV, limit {v.VminKv:F2}–{v.VmaxKv:F2} kV)" : "";
        Console.WriteLine(
            $"  Bus {v.BusId, -4}  Vm={v.Vm:F4} pu  ({kind}voltage, limit {v.Vmin:F3}–{v.Vmax:F3} pu){kvPart}"
        );
    }
}

// Thermal violations
if (thermalViols.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(Ruler("Thermal Violations"));
    foreach (var bf in thermalViols)
        Console.WriteLine(
            $"  Branch {bf.FromBusId, -4}→{bf.ToBusId, -4}  Loading={bf.LoadingPct:F1}%  "
                + $"(limit {bf.RateA * mva:F1} MVA)"
        );
}

// Summary
var bestBf = result
    .BranchFlows.Where(bf => !double.IsNaN(bf.LoadingPct))
    .OrderByDescending(bf => bf.LoadingPct)
    .FirstOrDefault();
var worstV =
    result.VoltageViolations.Count > 0
        ? result
            .VoltageViolations.OrderByDescending(v =>
                Math.Abs(v.Vm - (v.IsOverVoltage ? v.Vmax : v.Vmin))
            )
            .First()
        : null;

Console.WriteLine();
Console.WriteLine(Ruler("Balance"));
if (result.Balance is { } bal)
{
    Console.WriteLine(
        $"  {"Generation", -14}  {bal.TotalGenerationMw, 8:F1} MW   {bal.TotalGenerationMvar, 8:F1} MVAr"
    );
    Console.WriteLine(
        $"  {"Load", -14}  {bal.TotalLoadMw, 8:F1} MW   {bal.TotalLoadMvar, 8:F1} MVAr"
    );
    Console.WriteLine($"  {"Losses", -14}  {bal.TotalLossesMw, 8:F1} MW   ({bal.LossPct:F2} %)");
    if (Math.Abs(result.Lambda * mva) >= 0.05)
        Console.WriteLine($"  {"Dist. slack λ", -14}  {result.Lambda * mva, +8:F1} MW");
}
else
{
    Console.WriteLine("  (not available — solver did not converge)");
}

if (result.QLimitBound.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(Ruler("Q-Limits Binding"));
    foreach (var (busId, atMax) in result.QLimitBound)
    {
        var gr = result.Generators.FirstOrDefault(g => g.BusId == busId);
        string lm = atMax ? $"{BY()}Qmax{R()}" : $"{BY()}Qmin{R()}";
        string gv = gr is not null ? $"  Pg={gr.Pg:F1} MW  Qg={gr.Qg:F1} MVAr" : "";
        Console.WriteLine($"  Bus {busId, -4}  [{lm}]{gv}");
    }
}

Console.WriteLine();
Console.WriteLine(Ruler("Solver"));
Console.WriteLine(
    $"  {"Iterations", -14}  {result.Iterations}   max|f| = {result.MaxMismatch:E2} pu"
);
if (worstV is not null)
{
    string kind = worstV.IsOverVoltage ? "over" : "under";
    string kvNote = worstV.BaseKv > 0 ? $"  ({worstV.VmKv:F2} kV)" : "";
    Console.WriteLine(
        $"  {"Worst voltage", -14}  Bus {worstV.BusId, -5} {worstV.Vm:F4} pu{kvNote}  ({kind}voltage)"
    );
}
if (bestBf is not null)
    Console.WriteLine(
        $"  {"Max loading", -14}  {bestBf.FromBusId}→{bestBf.ToBusId}   {bestBf.LoadingPct:F1} %"
    );

return result.Converged ? 0 : 2;
