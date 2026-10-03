using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.AddFieldDefinition;

public record AddFieldDefinitionCommand(
    Guid FormDefinitionId, string Code, string Label, FieldType FieldType, bool IsRequired,
    string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson,
    string? VisibleWhenFieldCode = null, string? VisibleWhenValuesJson = null,
    string? FilterByFieldCode = null, Guid? DynamicOptionsSourceFormDefinitionId = null,
    string? DynamicOptionsSourceFieldCode = null) : IRequest<Guid>;

public class AddFieldDefinitionCommandValidator : AbstractValidator<AddFieldDefinitionCommand>
{
    // Postgres's 63-byte identifier limit, minus the "lkp_" prefix DynamicSchemaService
    // builds a reporting-view join alias from for every Lookup field
    // (RefreshReportingViewAsync) - a Code right at the plain 63-char limit would overflow
    // once prefixed. Only Lookup fields go through that prefixing, so only they need the
    // tighter cap.
    private const int MaxLookupCodeLength = 63 - 4;

    public AddFieldDefinitionCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(63);
        RuleFor(x => x.Code).MaximumLength(MaxLookupCodeLength).When(x => x.FieldType == FieldType.Lookup)
            .WithMessage($"Lookup field codes can be at most {MaxLookupCodeLength} characters.");
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OptionsJson).NotEmpty()
            .When(x => x.FieldType == FieldType.Dropdown && string.IsNullOrWhiteSpace(x.DynamicOptionsSourceFieldCode))
            .WithMessage("Dropdown fields require options (a static list, or a dynamic source).");
        RuleFor(x => x.LookupFormDefinitionId).NotEmpty().When(x => x.FieldType == FieldType.Lookup)
            .WithMessage("Lookup fields require a target form.");
        RuleFor(x => x.DynamicOptionsSourceFormDefinitionId).NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.DynamicOptionsSourceFieldCode))
            .WithMessage("A dynamic options source needs a target form.");
        RuleFor(x => x.DynamicOptionsSourceFieldCode).NotEmpty()
            .When(x => x.DynamicOptionsSourceFormDefinitionId.HasValue)
            .WithMessage("A dynamic options source needs a target field code.");
        RuleFor(x => x.DynamicOptionsSourceFormDefinitionId).Empty()
            .When(x => x.FieldType != FieldType.Dropdown && x.DynamicOptionsSourceFormDefinitionId.HasValue)
            .WithMessage("Only a Dropdown field can source its options dynamically from another form.");
        RuleFor(x => x.VisibleWhenValuesJson).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.VisibleWhenFieldCode))
            .WithMessage("A visibility condition needs at least one allowed value.");
        RuleFor(x => x.VisibleWhenFieldCode).NotEqual(x => x.Code).When(x => !string.IsNullOrWhiteSpace(x.VisibleWhenFieldCode))
            .WithMessage("A field can't control its own visibility.");
        RuleFor(x => x.FilterByFieldCode).Empty().When(x => x.FieldType != FieldType.Lookup && !string.IsNullOrWhiteSpace(x.FilterByFieldCode))
            .WithMessage("Only a Lookup field can filter its candidates by another field.");
        RuleFor(x => x.FilterByFieldCode).NotEqual(x => x.Code).When(x => !string.IsNullOrWhiteSpace(x.FilterByFieldCode))
            .WithMessage("A field can't filter itself.");
    }
}

public class AddFieldDefinitionCommandHandler : IRequestHandler<AddFieldDefinitionCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public AddFieldDefinitionCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task<Guid> Handle(AddFieldDefinitionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();

        // Cross-aggregate half of the Lookup-filter check: FormVersion.AddField already
        // confirmed FilterByFieldCode names a sibling on THIS form version; this confirms the
        // TARGET form (where the filtered candidates actually come from) has an active field
        // with that identical Code too - without it, the filter would silently match nothing
        // once used (FormRenderer would filter by a column that doesn't exist on the target).
        if (!string.IsNullOrWhiteSpace(request.FilterByFieldCode))
        {
            var lookupTarget = await _db.FormDefinitions
                .Include(f => f.Versions).ThenInclude(v => v.Fields)
                .SingleOrDefaultAsync(f => f.Id == request.LookupFormDefinitionId, cancellationToken);
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

        // Same shape as the Lookup-filter cross-aggregate check above, for a dynamic-options
        // source: confirms the SOURCE form actually has an active field with this Code, since
        // that's where the live distinct-values query will read from (see
        // GetFieldDynamicOptionsQuery) - without this, the field would silently always offer
        // an empty option list once used.
        if (!string.IsNullOrWhiteSpace(request.DynamicOptionsSourceFieldCode))
        {
            var optionsSourceForm = await _db.FormDefinitions
                .Include(f => f.Versions).ThenInclude(v => v.Fields)
                .SingleOrDefaultAsync(f => f.Id == request.DynamicOptionsSourceFormDefinitionId, cancellationToken);
            var sourceHasMatchingField = optionsSourceForm?.GetPublishedVersion()?.Fields
                .Any(f => f.IsActive && f.Code == request.DynamicOptionsSourceFieldCode) ?? false;

            if (!sourceHasMatchingField)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.DynamicOptionsSourceFieldCode),
                        $"The source form doesn't have an active '{request.DynamicOptionsSourceFieldCode}' field - " +
                        "a dynamic options source needs a field with that code on the source form.")
                });
        }

        Platform.Domain.Forms.FieldDefinition field;
        try
        {
            field = target.Version.AddField(
                request.Code, request.Label, request.FieldType, request.IsRequired,
                request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson,
                request.VisibleWhenFieldCode, request.VisibleWhenValuesJson, request.FilterByFieldCode,
                request.DynamicOptionsSourceFormDefinitionId, request.DynamicOptionsSourceFieldCode);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("a visibility condition can only depend"))
        {
            // AddField's "does this sibling exist" check throws the same exception type as
            // the duplicate-code check right below it - distinguished by message text (set
            // deliberately in FormVersion.AddField) rather than just "VisibleWhenFieldCode was
            // sent", so a request that happens to send both a duplicate Code AND a visibility
            // condition still gets the duplicate-code error, not this one.
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.VisibleWhenFieldCode), ex.Message)
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("a Lookup filter can only depend"))
        {
            // Same shape, same reason, for the Lookup-filter sibling-exists check.
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.FilterByFieldCode), ex.Message)
            });
        }
        catch (ArgumentException ex) when (ex.ParamName is null)
        {
            // FieldDefinition.Create's NormalizeColumnName throws this specific shape of
            // ArgumentException (no ParamName) when Code doesn't normalize to a valid SQL
            // identifier - most commonly non-Latin input, since Code becomes a physical
            // column name and can't be. The other ArgumentExceptions AddField/Create can
            // throw (missing OptionsJson/LookupFormDefinitionId) always set ParamName, so
            // this guard is what keeps this catch scoped to the Code case specifically.
            // Code is given a stable identity ("form.field.codeMustBeLatin") rather than
            // just a message so the frontend can show this fully localized, not just in
            // whatever language the backend happens to write English strings in.
            throw new Common.Exceptions.ValidationException(
                "form.field.codeMustBeLatin",
                "Code must be Latin letters, digits, and underscores only, starting with a letter " +
                "(e.g. 'quantity_received') - it becomes a database column name. Use 'Field name' for " +
                "the human-readable label shown on the form, which can be in any language.");
        }

        catch (InvalidOperationException ex)
        {
            // Duplicate code on this version. Cheap to hit now that fields can be added
            // straight to a published form from more than one place at once.
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.Code), ex.Message)
            });
        }

        _db.FieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);

        // On a published form the column has to exist before anyone can submit against the
        // new field, so the DDL runs now rather than waiting for a publish that may never
        // come. Existing rows get NULL (AddColumnAsync always emits a nullable column, even
        // for a required field - there's nothing to backfill, and required-ness is enforced
        // at submission time). An unpublished form keeps the original behaviour exactly:
        // metadata only, all schema work batched into its first publish.
        if (target.IsLive && field.FieldType != FieldType.Attachment)
        {
            await _schemaService.AddColumnForFieldAsync(target.LiveTableName, field, cancellationToken);

            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }

        return field.Id;
    }
}
