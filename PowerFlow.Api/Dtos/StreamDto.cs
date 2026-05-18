namespace PowerFlow.Api.Dtos;

// SSE event shapes for the convergence streaming endpoint.
// The "type" field discriminates which concrete event was sent.

public sealed record IterEventDto(
    string Type, // always "iter"
    int Iter,
    double Mismatch, // pu ‖f‖∞ at this iteration
    BusTypeChangeDto[] BusTypeChanges
);

public sealed record BusTypeChangeDto(
    int BusId,
    string From, // "PV" | "PQ"
    string To,
    string Reason
); // "Qmax" | "Qmin" | "Restored"

public sealed record ResultEventDto(
    string Type, // always "result"
    SolveResultDto Result
);

public sealed record ErrorEventDto(
    string Type, // always "error"
    string Message
);
