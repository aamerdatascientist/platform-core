using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.UpdateFieldLabel;

/// <summary>
/// Safe operation 1 of 3: changes only the human-readable Label. The field's Code - and so
/// its physical column, and every value in it - is untouched, which is why this needs no
/// confirmation even on a published form with live data.
/// </summary>
/// <remarks>LabelEn (redesign §9.1): null leaves the English label as it is (older callers send
/// only Label); an empty string clears it.</remarks>
public record UpdateFieldLabelCommand(Guid FormDefinitionId, Guid FieldDefinitionId, string Label, string? LabelEn = null) : IRequest;

public class UpdateFieldLabelCommandValidator : AbstractValidator<UpdateFieldLabelCommand>
{
    public UpdateFieldLabelCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LabelEn).MaximumLength(200);
    }
}

public class UpdateFieldLabelCommandHandler : IRequestHandler<UpdateFieldLabelCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public UpdateFieldLabelCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(UpdateFieldLabelCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        field.UpdateLabel(request.Label);
        if (request.LabelEn is not null) field.UpdateLabelEn(request.LabelEn);
        await _db.SaveChangesAsync(cancellationToken);

        // The reporting view's column aliases are built from Label, so on a published form a
        // relabel that skipped this would leave Power BI/Metabase showing the old name
        // indefinitely - the metadata and the view would just quietly disagree. Deliberately
        // after SaveChanges: a view refresh that fails shouldn't roll back a rename that's
        // otherwise valid, and the next publish (or relabel) regenerates the view anyway.
        if (target.IsLive)
        {
            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }
    }
}
