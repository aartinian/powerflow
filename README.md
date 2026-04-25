# PowerFlow

![.NET 10](https://img.shields.io/badge/.NET-10-blue) ![License: MIT](https://img.shields.io/badge/License-MIT-green)
 
A steady-state AC load flow solver implemented in C#.
 
Solves Newton-Raphson load flow on standard IEEE test cases, validated against MATPOWER results.
 
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
dotnet run --project PowerFlow.Runner -- --input cases/ieee14.m
```
 
## Reference
 
Zimmerman et al., *MATPOWER: Steady-State Operations, Planning and Analysis Tools for Power Systems Research and Education*, IEEE 2011
