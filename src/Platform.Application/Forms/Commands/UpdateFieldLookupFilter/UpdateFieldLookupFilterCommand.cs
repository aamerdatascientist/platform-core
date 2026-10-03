using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Exceptions;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.UpdateFieldLookupFilter;

/// <summary>
/// Safe operation (same bucket as UpdateFieldVisibility/UpdateFieldLabel): sets or clears
/// which sibling field's current value narrows a Lookup field's candidate rows down to the
/// ones belonging to it (e.g. a "zone" Lookup filtered by this form's own "project" field, so
/// it only offers zones for the currently-selected project instead of every zone from every
/// project). Pure metadata - never touches the physical column (still just a GUID either
/// way), so no confirmation is needed even on a published form with live data. Passing
/// null/empty clears the filter, making the Lookup show every target-form row again (the
/// default for every Lookup field that never had one).
/// </summary>
public record UpdateFieldLookupFilterCommand(
    Guid FormDefinitionId, Guid FieldDefinitionId, string? FilterByFieldCode) : IRequest;

public class UpdateFieldLookupFilterCommandValidator : AbstractValidator<UpdateFieldLookupFilterCommand>
{
    public UpdateFieldLookupFilterCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class UpdateFieldLookupFilterCommandHandler : IRequestHandler<UpdateFieldLookupFilterCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateFieldLookupFilterCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFieldLookupFilterCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (!string.IsNullOrWhiteSpace(request.FilterByFieldCode))
        {
            if (field.FieldType != FieldType.Lookup)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.FilterByFieldCode),
                        "Only a Lookup field can filter its candidates by another field.")
                });

            // Same sibling-exists guard AddFieldDefinitionCommand gets from FormVersion.AddField
            // itself - this command edits a field already on the version rather than adding
            // one, so it has to run the check explicitly instead of getting it for free.
            if (!target.Version.HasField(request.FilterByFieldCode))
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.FilterByFieldCode),
                        $"'{request.FilterByFieldCode}' isn't a field on this form version - a Lookup filter " +
                        "can only depend on a field that already exists here.")
                });

            // Cross-aggregate half of the same check AddFieldDefinitionCommand runs: the
            // TARGET form (field.LookupFormDefinitionId) needs an active field with this same
            // Code too, or the filter would silently match nothing once used.
            var lookupTarget = await _db.FormDefinitions
                .Include(f => f.Versions).ThenInclude(v => v.Fields)
                .SingleOrDefaultAsync(f => f.Id == field.LookupFormDefinitionId, cancellationToken);
            var targetHasMatchingField = lookupTarget?.GetPublishedVersion()?.Fields
                .Any(f => f.IsActive && f.Code == request.FilterByFieldCode) ?? false;

            if (!targetHasMatchingField)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.FilterByFieldCode),
                        $"The target form doesn't have an active '{request.FilterByFieldCode}' field - a Lookup " +
                        "filter needs a field with that same code on both this form and the target form.")
                });
        }

        try
        {
            field.SetLookupFilter(request.FilterByFieldCode);
        }
        catch (ArgumentException ex)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.FilterByFieldCode), ex.Message)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        // No reporting-view refresh needed: like VisibleWhenFieldCode, FilterByFieldCode feeds
        // nothing in RefreshReportingViewAsync's column-alias logic.
    }
}
