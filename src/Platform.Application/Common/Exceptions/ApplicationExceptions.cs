using FluentValidation.Results;

namespace Platform.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException() : base("One or more validation failures occurred.") =>
        Errors = new Dictionary<string, string[]>();

    public ValidationException(IEnumerable<ValidationFailure> failures) : this() =>
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
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
