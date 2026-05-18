namespace PowerFlow.Api.Dtos;

public sealed record CaseMetaDto(string Id, string Label, int Buses, int Branches, int Generators);

public sealed record ParseRequestDto(string Content);
