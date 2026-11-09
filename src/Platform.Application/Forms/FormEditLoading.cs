using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms;

public static class FormEditLoading
{
    /// <summary>
    /// Loads a form with every version's fields AND steps - what the label/step editing
    /// commands need (redesign §9.1/§9.2). 404 when it doesn't exist.
    /// </summary>
    public static async Task<FormDefinition> LoadFormForEditAsync(
        this IApplicationDbContext db, Guid formDefinitionId, CancellationToken cancellationToken) =>
        await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .Include(f => f.Versions).ThenInclude(v => v.Steps)
            .SingleOrDefaultAsync(f => f.Id == formDefinitionId, cancellationToken)
        ?? throw new NotFoundException(nameof(FormDefinition), formDefinitionId);

    /// <summary>A step on the version being edited, or a clean 404.</summary>
    public static FormStep FindStepOrNotFound(this FormVersion version, Guid stepId) =>
        version.Steps.SingleOrDefault(s => s.Id == stepId)
        ?? throw new NotFoundException(nameof(FormStep), stepId);

    public static ValidationException Invalid(string property, string message) =>
        new(new[] { new FluentValidation.Results.ValidationFailure(property, message) });
}
