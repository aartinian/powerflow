namespace PowerFlow.Api.Dtos;

public sealed record NetworkDto(
    string? Name,
    double BaseMva,
    BusDto[] Buses,
    BranchDto[] Branches,
    GeneratorDto[] Generators
);

public sealed record BusDto(
    int Id,
    string Type, // "PQ" | "PV" | "Slack" | "Isolated"
    double Pd, // MW
    double Qd, // MVAr
    double Gs, // shunt conductance, MW at 1 pu
    double Bs, // shunt susceptance, MVAr at 1 pu
    double Vm, // pu — warm-start input, not the solved value
    double Va, // degrees — warm-start input
    double BaseKv, // kV — reporting only
    double Vmax, // pu
    double Vmin
); // pu

public sealed record BranchDto(
    int Index, // 0-based stable identity (no natural ID in MATPOWER)
    int FromBusId,
    int ToBusId,
    double R, // pu
    double X, // pu
    double B, // pu total line charging
    double TapRatio, // ≥ 1.0; already normalised from 0 → 1.0 at parse time
    double PhaseShift, // degrees
    double RateA, // MVA, 0 = unconstrained
    double RateB, // MVA
    double RateC, // MVA
    double Angmin, // degrees
    double Angmax, // degrees
    bool IsInService
);

public sealed record GeneratorDto(
    int Index, // 0-based stable identity
    int BusId,
    double Pg, // MW — scheduled dispatch
    double Qg, // MVAr — initial value only; solver overwrites at PV/Slack
    double Qmax, // MVAr
    double Qmin, // MVAr
    double Vg, // pu setpoint
    double Pmax, // MW
    double Pmin, // MW
    bool IsInService,
    double MBase
); // MVA, 0 = use system base
