using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.RenameFieldCode;

/// <summary>
/// Risky operation 1 of 3: changes a field's Code, which IS the physical column name.
///
/// This replaces the old remove-and-re-add workaround, which was never a rename at all - it
/// dropped the field's metadata and created a new field with a new column, so every existing
/// value was stranded in an orphaned column and the field jumped to the end of the form. A
/// real ALTER TABLE ... RENAME COLUMN keeps the data and the position.
///
/// Anything outside this database that refers to the column by name - a Power BI query
/// against the raw table, an external integration - still breaks. That's the consequence the
/// caller's confirmation needs to state, and it's why this isn't in the safe group.
/// </summary>
public record RenameFieldCodeCommand(Guid FormDefinitionId, Guid FieldDefinitionId, string NewCode) : IRequest;

public class RenameFieldCodeCommandValidator : AbstractValidator<RenameFieldCodeCommand>
{
    private const int MaxLookupCodeLength = 63 - 4;

    public RenameFieldCodeCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.NewCode).NotEmpty().MaximumLength(63);
    }
}

public class RenameFieldCodeCommandHandler : IRequestHandler<RenameFieldCodeCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public RenameFieldCodeCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(RenameFieldCodeCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);
        var oldCode = field.Code;

        // Same cap AddFieldDefinitionCommandValidator applies, for the same reason - a Lookup
        // field's Code gets an "lkp_" prefix when the reporting view builds its join alias,
        // so renaming one right up to 63 would overflow on the next view refresh. Checked
        // here rather than in the validator because it depends on the field's type, which the
        // validator can't see without a database round-trip.
        if (field.FieldType == FieldType.Lookup && request.NewCode.Trim().Length > 63 - 4)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.NewCode), $"Lookup field codes can be at most {63 - 4} characters.")
            });

        try
        {
            field.RenameCode(request.NewCode);
        }
        catch (ArgumentException ex)
        {
            throw new Common.Exceptions.ValidationException(
                "form.field.codeMustBeLatin",
                ex.Message);
        }

        if (field.Code == oldCode) return;

        if (target.Version.Fields.Any(f => f.Id != field.Id && f.Code == field.Code))
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.NewCode), $"Field code '{field.Code}' already exists on this form.")
            });

        if (!target.IsLive || field.FieldType == FieldType.Attachment)
        {
            // Nothing physical to rename: either the form has never been published (no table
            // yet - its first publish will create the column under the new name), or the
            // field is an Attachment, which never gets a column at all.
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        await _schemaService.RenameColumnAsync(target.LiveTableName, oldCode, field.Code, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The column is renamed but the metadata save failed, so the two now disagree -
            // and metadata pointing at a column name that no longer exists breaks every
            // submission against this form. Rename the column back so the form stays working,
            // then report the original failure. This is the one operation here where the
            // compensating action is itself safe and lossless; a type change or a drop isn't.
            await _schemaService.RenameColumnAsync(target.LiveTableName, field.Code, oldCode, CancellationToken.None);
            throw;
        }

        var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
        await _schemaService.RefreshReportingViewAsync(
            formDefinition, target.Version, lookupTargets, cancellationToken);
    }
}
