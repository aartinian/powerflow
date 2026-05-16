namespace PowerFlow.Api.Dtos;

public sealed record ContingencyResultDto(
    int BranchIndex,
    int FromBusId,
    int ToBusId,
    bool Converged,
    double? MaxLoadingPct,
    int VoltageViolationCount,
    int BranchOverloadCount, // branches with loading > 100%
    OverloadDto[] OverloadedBranches,
    VoltageViolationDto[] VoltageViolations
);

public sealed record OverloadDto(int FromBusId, int ToBusId, double LoadingPct);
