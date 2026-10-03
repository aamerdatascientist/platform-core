using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Exceptions;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.UpdateFieldDynamicOptionsSource;

/// <summary>
/// Safe operation (same bucket as UpdateFieldLookupFilter/UpdateFieldVisibility): points a
/// Dropdown field's option list at another form's field (its distinct, non-null submitted
/// values, computed live - see GetFieldDynamicOptionsQuery) instead of its own static
/// OptionsJson list, or clears it back to static. Pure metadata - never touches the physical
/// column (still just a plain string either way), so no confirmation is needed even on a
/// published form with live data. Passing both null/empty clears the source, making the
/// Dropdown fall back to its own OptionsJson again (the default for every Dropdown that never
/// had one).
/// </summary>
public record UpdateFieldDynamicOptionsSourceCommand(
    Guid FormDefinitionId, Guid FieldDefinitionId, Guid? SourceFormDefinitionId, string? SourceFieldCode) : IRequest;

public class UpdateFieldDynamicOptionsSourceCommandValidator : AbstractValidator<UpdateFieldDynamicOptionsSourceCommand>
{
    public UpdateFieldDynamicOptionsSourceCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.SourceFormDefinitionId).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.SourceFieldCode))
            .WithMessage("A dynamic options source needs a target form.");
        RuleFor(x => x.SourceFieldCode).NotEmpty().When(x => x.SourceFormDefinitionId.HasValue)
            .WithMessage("A dynamic options source needs a target field code.");
    }
}

public class UpdateFieldDynamicOptionsSourceCommandHandler : IRequestHandler<UpdateFieldDynamicOptionsSourceCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateFieldDynamicOptionsSourceCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFieldDynamicOptionsSourceCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (!string.IsNullOrWhiteSpace(request.SourceFieldCode))
        {
            if (field.FieldType != FieldType.Dropdown)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.SourceFieldCode),
                        "Only a Dropdown field can source its options dynamically from another form.")
                });

            // Cross-aggregate check, same shape as UpdateFieldLookupFilterCommand's: the SOURCE
            // form needs an active field with this Code, or the field would silently always
            // offer an empty option list once used.
            var sourceForm = await _db.FormDefinitions
                .Include(f => f.Versions).ThenInclude(v => v.Fields)
                .SingleOrDefaultAsync(f => f.Id == request.SourceFormDefinitionId, cancellationToken);
            var sourceHasMatchingField = sourceForm?.GetPublishedVersion()?.Fields
                .Any(f => f.IsActive && f.Code == request.SourceFieldCode) ?? false;

            if (!sourceHasMatchingField)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.SourceFieldCode),
                        $"The source form doesn't have an active '{request.SourceFieldCode}' field - a dynamic " +
                        "options source needs a field with that code on the source form.")
                });
        }

        try
        {
            field.SetDynamicOptionsSource(request.SourceFormDefinitionId, request.SourceFieldCode);
        }
        catch (ArgumentException ex)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.SourceFieldCode), ex.Message)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        // No reporting-view refresh needed: like FilterByFieldCode, this metadata feeds nothing
        // in RefreshReportingViewAsync's column-alias logic - the physical column is still a
        // plain string either way.
    }
}
