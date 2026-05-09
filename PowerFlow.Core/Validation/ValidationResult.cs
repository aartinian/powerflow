namespace PowerFlow.Core.Validation;

/// <summary>
/// The outcome of <see cref="NetworkValidator.Validate"/>.
/// <see cref="IsValid"/> is <c>false</c> only when at least one
/// <see cref="ValidationSeverity.Error"/> entry is present; warnings alone do not
/// make a result invalid. Call <see cref="ThrowIfInvalid"/> for a fire-and-forget usage.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>A result with no errors or warnings.</summary>
    public static readonly ValidationResult Ok = new([]);

    /// <summary>All issues found, both errors and warnings, in the order they were detected.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>Errors only (severity = Error); does not include warnings.</summary>
    public IEnumerable<ValidationError> FatalErrors =>
        Errors.Where(e => e.Severity == ValidationSeverity.Error);

    /// <summary>Warnings only; does not include blocking errors.</summary>
    public IEnumerable<ValidationError> Warnings =>
        Errors.Where(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// <c>true</c> when there are no <see cref="ValidationSeverity.Error"/> entries.
    /// Warnings do not affect validity.
    /// </summary>
    public bool IsValid => !Errors.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>Initializes a new validation result with the specified list of issues.</summary>
    public ValidationResult(IReadOnlyList<ValidationError> errors) => Errors = errors;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <see cref="IsValid"/> is
    /// <c>false</c>, with all error messages included in the exception message.
    /// </summary>
    public void ThrowIfInvalid()
    {
        if (IsValid)
            return;
        var lines = string.Join("\n", FatalErrors.Select(e => $"  [{e.Code}] {e.Message}"));
        throw new InvalidOperationException($"Network validation failed:\n{lines}");
    }
}
