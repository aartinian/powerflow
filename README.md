# PowerFlow

![.NET 10](https://img.shields.io/badge/.NET-10-blue) ![License: MIT](https://img.shields.io/badge/License-MIT-green)

A steady-state power flow solver in C#. Supports full AC Newton-Raphson and linearised DC, validated against MATPOWER.

## Validated test cases

| Case | Buses | Branches | Generators |
|------|------:|---------:|-----------:|
| IEEE 14-bus  |  14 |  20 |   5 |
| IEEE 118-bus | 118 | 186 |  54 |
| IEEE 300-bus | 300 | 411 |  69 |

## Project structure

```
PowerFlow/
├── PowerFlow.Core/    # Models, parser, solver, validator
├── PowerFlow.Runner/  # Console entry point
└── PowerFlow.Tests/   # xUnit tests
```

## Getting started

```bash
git clone https://github.com/aartinian/powerflow.git
cd powerflow
dotnet restore && dotnet build && dotnet test
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
```

**Options**

| Flag | Default | Description |
|------|---------|-------------|
| `--flat-start` | off | Force Vm=1 pu, Va=0° initial guess |
| `--distributed-slack` | off | Share imbalance by Pmax participation |
| `--dc` | off | Linearised DC power flow |
| `--no-limits` | off | Disable Q-limit enforcement |
| `--tol <ε>` | `1e-6` | Convergence tolerance |
| `--max-iter <n>` | `50` | NR iteration cap |
| `--no-color` | off | Disable ANSI colour output |
| `--no-buses` | off | Suppress the bus results table |
| `--no-branches` | off | Suppress the branch results table |
| `--summary-only` | off | Suppress both tables |

Exit code `0` = converged, `2` = did not converge, `1` = input error.

## Reference

Zimmerman et al., *MATPOWER: Steady-State Operations, Planning and Analysis Tools for Power Systems Research and Education*, IEEE 2011
