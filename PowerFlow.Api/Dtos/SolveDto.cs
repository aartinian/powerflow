namespace PowerFlow.Api.Dtos;

public sealed record SolveRequestDto(NetworkDto Network, SolveOptionsDto Options);

public sealed record SolveOptionsDto(
    string Mode, // "AC" | "DC"
    bool FlatStart,
    bool EnforceLimits,
    bool DistributedSlack,
    bool WarmStartFromDc,
    double Tolerance,
    int MaxIterations
);

public sealed record SolveResultDto(
    string Mode, // "AC" | "DC" — discriminator for nullable fields
    bool Converged,
    int Iterations,
    int OuterIterations,
    double MaxMismatch, // pu
    double? Lambda, // pu, non-null only when DistributedSlack was on
    SolvedBusDto[] Buses,
    SolvedBranchDto[] Branches,
    SolvedGeneratorDto[] Generators, // empty for DC
    VoltageViolationDto[] Violations, // empty for DC
    SystemBalanceDto? Balance
);

public sealed record SolvedBusDto(
    int BusId,
    double? Vm, // pu — null for DC (Vm ≡ 1 pu by assumption)
    double Va, // degrees
    double Pg, // MW
    double? Qg, // MVAr — null for DC
    double Pd, // MW echoed from input
    double? Qd
); // MVAr — null for DC

public sealed record SolvedBranchDto(
    int BranchIndex,
    int FromBusId,
    int ToBusId,
    double Pij, // MW
    double? Qij, // MVAr — null for DC
    double? Pji, // MW — null for DC (lossless: Pji = -Pij)
    double? Qji, // MVAr — null for DC
    double? LossMw, // MW — null for DC
    double? LoadingPct
); // % — null when RateA = 0

public sealed record SolvedGeneratorDto(
    int Index,
    int BusId,
    double Pg, // MW solved (may differ from input when DistributedSlack)
    double? Qg, // MVAr — null for DC
    bool IsAtQmax,
    bool IsAtQmin
);

public sealed record VoltageViolationDto(
    int BusId,
    double Vm, // pu
    bool IsOverVoltage,
    double Vmin, // pu
    double Vmax, // pu
    double BaseKv, // kV
    double VmKv
); // kV

public sealed record SystemBalanceDto(
    double TotalGenerationMw,
    double TotalLoadMw,
    double TotalLossesMw,
    double LossPct,
    double TotalGenerationMvar,
    double TotalLoadMvar,
    double TotalShuntMvar,
    double TotalLossesMvar
);
