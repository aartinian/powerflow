namespace PowerFlow.Core.Models;

/// <summary>
/// The outcome of <see cref="NetworkValidator.Validate"/>.
/// <see cref="IsValid"/> is <c>false</c> only when at least one
/// <see cref="ValidationSeverity.Error"/> entry is present; warnings alone do not
/// make a result invalid.  Call <see cref="ThrowIfInvalid"/> for a fire-and-forget usage.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>A result with no errors or warnings.</summary>
    public static readonly ValidationResult Ok = new([]);

    public IReadOnlyList<ValidationError> Errors { get; }

    public IEnumerable<ValidationError> FatalErrors =>
        Errors.Where(e => e.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationError> Warnings =>
        Errors.Where(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// <c>true</c> when there are no <see cref="ValidationSeverity.Error"/> entries.
    /// Warnings do not affect validity.
    /// </summary>
    public bool IsValid => !Errors.Any(e => e.Severity == ValidationSeverity.Error);

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
