using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.DeleteField;

/// <summary>
/// Risky operation 3 of 3, destructive half: physically DROPs the field's column and every
/// value ever recorded in it. There is no undo - not through this API, not through a later
/// publish, not from anything the application retains.
///
/// This is the escape hatch, not the everyday path. It exists for data that should never
/// have been collected (personal data captured in error, test pollution). Anything that just
/// needs to come off the form should use ArchiveFieldCommand, which keeps the history.
///
/// On a published form, ConfirmFieldCode has to match the field's own Code exactly. That's a
/// deliberate speed bump rather than real authorization - the point is that "delete this
/// field" can't be something a caller does by reflex or by sending one wrong Id.
///
/// On a form that has never been published, no confirmation is required and none is asked
/// for: there is no column and no submitted data, so removing a field is just un-typing
/// something you were still in the middle of writing. The gate protects data; where there
/// provably isn't any, it would only be friction.
/// </summary>
public record DeleteFieldCommand(Guid FormDefinitionId, Guid FieldDefinitionId, string? ConfirmFieldCode) : IRequest;

public class DeleteFieldCommandValidator : AbstractValidator<DeleteFieldCommand>
{
    public DeleteFieldCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        // ConfirmFieldCode is checked in the handler, not here - whether it's required at all
        // depends on whether the form is published, which the validator can't see.
    }
}

public class DeleteFieldCommandHandler : IRequestHandler<DeleteFieldCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public DeleteFieldCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(DeleteFieldCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (target.IsLive && !string.Equals(request.ConfirmFieldCode?.Trim(), field.Code, StringComparison.Ordinal))
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.ConfirmFieldCode),
                    $"Type '{field.Code}' exactly to confirm permanently deleting this field and its data.")
            });

        if (target.Version.Fields.Count(f => f.IsActive) == 1 && field.IsActive)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.FieldDefinitionId),
                    "This is the form's only remaining active field - a form with no fields can't be submitted " +
                    "or published. Add another field first, or delete the whole form.")
            });

        var code = field.Code;
        var isAttachment = field.FieldType == FieldType.Attachment;

        target.Version.RemoveField(field.Id);

        // Metadata first, column second - the deliberate opposite of the rename path's
        // ordering. Both orderings can be interrupted between the two stores, so each
        // operation is ordered so that the surviving half is the harmless one: a column with
        // no metadata pointing at it is invisible and inert (and can be dropped by hand
        // later), whereas metadata pointing at a column that's already gone breaks every
        // submission against the form. A rename can be compensated; a DROP can't, so it goes
        // last, once nothing can still fail after it.
        await _db.SaveChangesAsync(cancellationToken);

        if (target.IsLive && !isAttachment)
        {
            await _schemaService.DropColumnAsync(target.LiveTableName, code, cancellationToken);

            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }
    }
}
