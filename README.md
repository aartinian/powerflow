# PowerFlow

[![.NET CI](https://github.com/aartinian/powerflow/actions/workflows/dotnet.yml/badge.svg)](https://github.com/aartinian/powerflow/actions/workflows/dotnet.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

A steady-state AC/DC power flow solver in C#. Includes Newton-Raphson with sparse LU factorisation, Q-limit enforcement, distributed slack, DC warm-start, and MATPOWER `.m` case-file parsing. Validated against MATPOWER on five IEEE benchmark cases.

## Quick start — library

```csharp
using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;
using PowerFlow.Core.Solver;

// Parse a MATPOWER case file
var network = MatpowerParser.Parse("case14.m");

// Basic AC Newton-Raphson
var result = new NewtonRaphsonSolver().Solve(network);
Console.WriteLine($"Converged: {result.Converged}");
Console.WriteLine($"Losses:    {result.Balance?.TotalLossesMw:F2} MW");

// DC warm-start (better initial guess for hard cases)
var result2 = new NewtonRaphsonSolver { WarmStartFromDc = true }.Solve(network);

// Distributed slack (share imbalance across all generators by Pmax)
var result3 = new NewtonRaphsonSolver { DistributedSlack = true }.Solve(network);
Console.WriteLine($"λ = {result3.Lambda:F6} pu");

// Linearised DC power flow
var dcResult = new DcPowerFlowSolver().Solve(network);
```

## AC vs DC

| | AC Newton-Raphson | DC (linearised) |
|---|---|---|
| Variables | Vm, Va | Va only (Vm ≈ 1 pu) |
| Solves for | P, Q balance | P balance |
| Losses | ✅ computed | ❌ lossless |
| Reactive power | ✅ full Q model | ❌ ignored |
| Speed | iterative (≈ 3–6 iters) | one sparse LU |

## Validated test cases

| Case | Buses | Branches | Generators | AC iters |
|------|------:|---------:|-----------:|---------:|
| IEEE 14-bus  |  14 |  20 |   5 | 3 |
| IEEE 30-bus  |  30 |  41 |   6 | 3 |
| IEEE 57-bus  |  57 |  80 |   7 | 3 |
| IEEE 118-bus | 118 | 186 |  54 | 4 |
| IEEE 300-bus | 300 | 411 |  69 | 6 |

All five cases match MATPOWER `runpf` results to |ΔVm| < 1 × 10⁻⁶ pu and |ΔVa| < 1 × 10⁻⁵°.

## Validation workflow

```csharp
using PowerFlow.Core.Validation;

var result = NetworkValidator.Validate(network);
if (!result.IsValid)
{
    foreach (var err in result.Errors)
        Console.WriteLine($"[{err.Severity}] {err.Code}: {err.Message}");
}
// Or throw immediately:
NetworkValidator.Validate(network).ThrowIfInvalid();
```

Checks include: missing slack bus, broken bus references, network islands, invalid tap ratios, phase-shift range, P-limit violations, conflicting Vg setpoints, and duplicate bus IDs.

## Project structure

```
PowerFlow/
├── PowerFlow.Core/    # Models, parser, solver, validator
├── PowerFlow.Runner/  # Console entry point
└── PowerFlow.Tests/   # xUnit tests (193 tests)
```

## Getting started — CLI

```bash
git clone https://github.com/aartinian/powerflow.git
cd powerflow
dotnet restore && dotnet build && dotnet test
dotnet run --project PowerFlow.Runner
```

## Runner

```bash
# Bundled IEEE 14-bus demo
dotnet run --project PowerFlow.Runner

# Any MATPOWER case file
dotnet run --project PowerFlow.Runner -- case118.m

# DC power flow
dotnet run --project PowerFlow.Runner -- --dc case118.m

# Distributed slack, custom tolerance
dotnet run --project PowerFlow.Runner -- --distributed-slack --tol 1e-8 case118.m

# DC warm-start (seed AC initial angles from DC solution)
dotnet run --project PowerFlow.Runner -- --warm-start case300.m
```

**Options**

| Flag | Default | Description |
|------|---------|-------------|
| `--flat-start` | off | Force Vm = 1 pu, Va = 0° initial guess |
| `--warm-start` | off | Seed AC initial Va from a DC solve |
| `--distributed-slack` | off | Share imbalance by Pmax participation |
| `--dc` | off | Linearised DC power flow |
| `--no-limits` | off | Disable Q-limit enforcement |
| `--tol <ε>` | `1e-6` | Convergence tolerance (pu) |
| `--max-iter <n>` | `50` | NR iteration cap |
| `--no-color` | off | Disable ANSI colour output |
| `--no-buses` | off | Suppress the bus results table |
| `--no-branches` | off | Suppress the branch results table |
| `--summary-only` | off | Suppress both tables |

Exit code `0` = converged, `2` = did not converge, `1` = input error.

## Known limitations

- Single-phase positive-sequence model only (no three-phase, no unbalanced)
- No optimal power flow (OPF) — fixed dispatch, solve for voltages/flows
- No load-tap-changer (LTC) automatic tap control
- No switched shunts (discrete shunt control)
- No multi-area interchange constraints
- Generator P-limit violations are flagged by the validator but not redispatched
- When multiple generators share a PV bus with different Vg setpoints, last-generator-wins
- PSS/E `.raw` and CIM formats not supported; MATPOWER `.m` only

## Reference

Zimmerman et al., *MATPOWER: Steady-State Operations, Planning and Analysis Tools for Power Systems Research and Education*, IEEE Transactions on Power Systems, 2011.
