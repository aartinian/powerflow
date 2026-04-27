# PowerFlow

![.NET 10](https://img.shields.io/badge/.NET-10-blue) ![License: MIT](https://img.shields.io/badge/License-MIT-green)
 
A steady-state AC load flow solver implemented in C#.

Solves Newton-Raphson load flow on standard IEEE test cases, validated against MATPOWER results.

**Features**
- Full Newton-Raphson solver (polar form, scaled Jacobian)
- PV→PQ bus switching with Q-limit enforcement
- Branch flows and losses (π model)
- MATPOWER `.m` file parser

**Validated test cases**

| Case | Buses | Branches | Generators |
|------|------:|--------:|----------:|
| IEEE 14-bus | 14 | 20 | 5 |
| IEEE 30-bus | 30 | 41 | 6 |
| IEEE 57-bus | 57 | 80 | 7 |
| IEEE 118-bus | 118 | 186 | 54 |

## Project Structure

```
PowerFlow/
├── PowerFlow.Core/       # Models, solver, parser
├── PowerFlow.Runner/     # Console entry point
└── PowerFlow.Tests/      # xUnit tests, IEEE validation
```

## Getting Started

```bash
git clone https://github.com/aartinian/powerflow.git
cd powerflow
dotnet restore
dotnet build
dotnet test
```

## Running a Load Flow

```bash
# Run the bundled IEEE 14-bus demo
dotnet run --project PowerFlow.Runner

# Run any MATPOWER case file
dotnet run --project PowerFlow.Runner -- path/to/case.m
```
 
## Reference
 
Zimmerman et al., *MATPOWER: Steady-State Operations, Planning and Analysis Tools for Power Systems Research and Education*, IEEE 2011
