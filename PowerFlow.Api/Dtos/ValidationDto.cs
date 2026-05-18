namespace PowerFlow.Api.Dtos;

public sealed record ValidationResultDto(bool IsValid, ValidationErrorDto[] Errors);

public sealed record ValidationErrorDto(
    string Code,
    string Message,
    string Severity // "Error" | "Warning"
);
