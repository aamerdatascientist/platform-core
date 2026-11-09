using FluentValidation.Results;

namespace Platform.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Machine-readable identity for this specific business-rule violation, e.g.
    /// "form.field.codeMustBeLatin" - lets the frontend map to a localized message via its
    /// i18n resources instead of displaying this exception's (English-only) Message
    /// directly. Null for validation failures that don't have a stable, translatable
    /// identity yet; the frontend falls back to the raw English text in that case.
    /// </summary>
    public string? Code { get; }

    public ValidationException() : base("One or more validation failures occurred.") =>
        Errors = new Dictionary<string, string[]>();

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base(string.Join(" ", failures.Select(f => f.ErrorMessage))) =>
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());

    /// <summary>For a single business-rule violation that has a stable, translatable identity.</summary>
    public ValidationException(string code, string message) : base(message)
    {
        Code = code;
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>Field-addressed failures that also carry a translatable identity (the first one's, when they differ).</summary>
    public ValidationException(string code, IEnumerable<ValidationFailure> failures) : this(failures) =>
        Code = code;
}

public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"Entity '{entityName}' ({key}) was not found.") { }
}

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string? reason = null)
        : base(reason ?? "You do not have permission to perform this action.") { }
}

/// <summary>
/// The caller isn't who they claim to be (a wrong email or password) - a 401, as opposed to
/// <see cref="ForbiddenAccessException"/> (403: known caller, not allowed). Sign-in used to
/// answer a wrong password with 403, which reads as "your account is blocked".
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string? reason = null)
        : base(reason ?? "Authentication failed.") { }
}
