using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.ChangeFieldType;

/// <summary>
/// Risky operation 2 of 3: changes a field's FieldType, which means changing its physical
/// column's type.
///
/// The rule this enforces: never attempt a lossy conversion. Before any DDL runs, every
/// existing value in the column is checked against the target type, and if any of them
/// wouldn't survive, the change is refused outright with the offending record Ids named -
/// rather than converting what it can and quietly nulling or truncating the rest.
/// </summary>
public record ChangeFieldTypeCommand(
    Guid FormDefinitionId, Guid FieldDefinitionId, FieldType NewFieldType,
    string? OptionsJson, Guid? LookupFormDefinitionId) : IRequest;

public class ChangeFieldTypeCommandValidator : AbstractValidator<ChangeFieldTypeCommand>
{
    public ChangeFieldTypeCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.OptionsJson).NotEmpty().When(x => x.NewFieldType == FieldType.Dropdown)
            .WithMessage("Dropdown fields require options.");
        RuleFor(x => x.LookupFormDefinitionId).NotEmpty().When(x => x.NewFieldType == FieldType.Lookup)
            .WithMessage("Lookup fields require a target form.");
    }
}

public class ChangeFieldTypeCommandHandler : IRequestHandler<ChangeFieldTypeCommand>
{
    /// <summary>Enough failing rows for someone to see the shape of the problem and go fix the
    /// data, without dumping a whole table's worth of Ids into an error message.</summary>
    private const int MaxReportedFailingRows = 10;

    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public ChangeFieldTypeCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(ChangeFieldTypeCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);
        var oldType = field.FieldType;

        if (oldType == request.NewFieldType)
        {
            // Not a no-op: Dropdown options and Lookup targets are edited through this same
            // command, so "same type, different options" is a legitimate metadata-only change.
            ApplyTypeChange(field, request);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Attachment has no physical column in either direction, so converting to or from it
        // isn't a type change at all - it's a different storage model, and the existing values
        // have nowhere to go. Refusing is the honest answer; archive the field and add a new
        // one if that's really what's wanted.
        if (oldType == FieldType.Attachment || request.NewFieldType == FieldType.Attachment)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.NewFieldType),
                    "Attachment fields can't be converted to or from another type - they're stored as files, " +
                    "not as a column. Archive this field and add a new one instead.")
            });

        if (target.IsLive)
        {
            var failingRowIds = await _schemaService.FindRowsFailingTypeChangeAsync(
                target.LiveTableName, field, request.NewFieldType, MaxReportedFailingRows, cancellationToken);

            if (failingRowIds.Count > 0)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.NewFieldType),
                        $"{failingRowIds.Count} or more existing records have a '{field.Label}' value that can't " +
                        $"be converted to {request.NewFieldType}. Fix or clear those values first. " +
                        $"Record Ids: {string.Join(", ", failingRowIds)}")
                });
        }

        ApplyTypeChange(field, request);

        if (target.IsLive)
            await _schemaService.ChangeColumnTypeAsync(
                target.LiveTableName, field, request.NewFieldType, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        if (target.IsLive)
        {
            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }
    }

    private static void ApplyTypeChange(Domain.Forms.FieldDefinition field, ChangeFieldTypeCommand request)
    {
        try
        {
            field.ChangeType(request.NewFieldType, request.OptionsJson, request.LookupFormDefinitionId);
        }
        catch (ArgumentException ex)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.NewFieldType), ex.Message)
            });
        }
    }
}
