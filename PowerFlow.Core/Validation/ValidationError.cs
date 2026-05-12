namespace PowerFlow.Core.Validation;

/// <summary>
/// Severity level for a <see cref="ValidationError"/>.
/// Only <see cref="Error"/> entries cause <see cref="ValidationResult.IsValid"/>
/// to return <c>false</c>; warnings are informational.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Likely data error but the solver may still produce a result (e.g. a
    /// generator dispatch outside its capacity band). Surfaced in the validation
    /// report but does not block solving.
    /// </summary>
    Warning,

    /// <summary>
    /// Structural problem that will cause the solver to fail or produce nonsense
    /// (e.g. no slack bus, broken bus reference, islanded network). Must be fixed
    /// before calling the solver.
    /// </summary>
    Error,
}

/// <summary>A single issue found by <see cref="NetworkValidator"/>.</summary>
public sealed class ValidationError
{
    /// <summary>
    /// Short machine-readable identifier for the class of problem, e.g.
    /// <c>NO_SLACK_BUS</c> or <c>NETWORK_ISLANDED</c>. Stable across versions;
    /// safe to match programmatically.
    /// </summary>
    public string Code { get; }

    /// <summary>Human-readable description with bus/branch IDs where relevant.</summary>
    public string Message { get; }

    /// <summary>Whether this is a blocking error or an informational warning.</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>Initializes a new validation issue with the specified code, message, and severity.</summary>
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
