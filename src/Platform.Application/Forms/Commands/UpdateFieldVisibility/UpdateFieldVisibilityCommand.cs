using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Exceptions;

namespace Platform.Application.Forms.Commands.UpdateFieldVisibility;

/// <summary>
/// Safe operation (same bucket as UpdateFieldLabel/ReorderFields): sets or clears which
/// sibling field controls this field's visibility, and which of that sibling's values make
/// it visible. Pure metadata - never touches the physical column, so no confirmation is
/// needed even on a published form with live data. Passing null/empty for both fields clears
/// the condition, making the field unconditionally visible again (the default for every
/// field that never had one).
/// </summary>
public record UpdateFieldVisibilityCommand(
    Guid FormDefinitionId, Guid FieldDefinitionId, string? VisibleWhenFieldCode, string? VisibleWhenValuesJson)
    : IRequest;

public class UpdateFieldVisibilityCommandValidator : AbstractValidator<UpdateFieldVisibilityCommand>
{
    public UpdateFieldVisibilityCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.VisibleWhenValuesJson).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.VisibleWhenFieldCode))
            .WithMessage("A visibility condition needs at least one allowed value.");
    }
}

public class UpdateFieldVisibilityCommandHandler : IRequestHandler<UpdateFieldVisibilityCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateFieldVisibilityCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFieldVisibilityCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        // Same sibling-exists guard AddFieldDefinitionCommand gets from FormVersion.AddField
        // itself - this command edits a field already on the version rather than adding one,
        // so it has to run the check explicitly instead of getting it for free.
        if (!string.IsNullOrWhiteSpace(request.VisibleWhenFieldCode) && !target.Version.HasField(request.VisibleWhenFieldCode))
            // Common.Exceptions.ValidationException, qualified deliberately: FluentValidation
            // also declares a ValidationException, and this file has `using FluentValidation;`
            // for AbstractValidator above, so the bare name is ambiguous (CS0104). Same
            // qualification AddFieldDefinitionCommand's handler uses for the same reason.
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.VisibleWhenFieldCode),
                    $"'{request.VisibleWhenFieldCode}' isn't a field on this form version - a visibility " +
                    "condition can only depend on a field that already exists here.")
            });

        try
        {
            field.SetVisibilityCondition(request.VisibleWhenFieldCode, request.VisibleWhenValuesJson);
        }
        catch (ArgumentException ex)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.VisibleWhenFieldCode), ex.Message)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        // No reporting-view refresh needed: unlike Label, visibility condition feeds nothing
        // in RefreshReportingViewAsync's column-alias logic.
    }
}
