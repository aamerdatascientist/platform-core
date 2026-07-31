namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Implement this on any MediatR request that operates on one specific form, and
/// FormAccessBehavior enforces role-based access automatically - no per-handler
/// access-check code needed, and no handler can forget to add one. Requests that return a
/// LIST of forms (like GetFormsListQuery) don't implement this - they need filtering logic,
/// not a single allow/deny check, and are handled separately in their own handler.
/// </summary>
public interface IFormScopedRequest
{
    Guid FormDefinitionId { get; }
}
