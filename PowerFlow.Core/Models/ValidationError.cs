namespace PowerFlow.Core.Models;

public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>A single issue found by <see cref="NetworkValidator"/>.</summary>
public sealed class ValidationError
{
    public string Code { get; }
    public string Message { get; }
    public ValidationSeverity Severity { get; }

    public ValidationError(
        string code,
        string message,
        ValidationSeverity severity = ValidationSeverity.Error
    )
    {
        Code = code;
        Message = message;
        Severity = severity;
    }
}
