using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.RestoreField;

/// <summary>
/// Un-archives a field: it reappears on the form and in the reporting view, with its column
/// and all its historical values exactly as they were left. This is what makes Archive a
/// genuinely safe default rather than a softer-sounding delete.
///
/// On a form published since the field was archived, the column may have been left behind
/// but never re-added to any newer version - so this re-runs the same additive ADD COLUMN the
/// add path uses, which is a no-op when the column is still there.
/// </summary>
public record RestoreFieldCommand(Guid FormDefinitionId, Guid FieldDefinitionId) : IRequest;

public class RestoreFieldCommandValidator : AbstractValidator<RestoreFieldCommand>
{
    public RestoreFieldCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class RestoreFieldCommandHandler : IRequestHandler<RestoreFieldCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public RestoreFieldCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(RestoreFieldCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (field.IsActive) return;

        field.Reactivate();
        await _db.SaveChangesAsync(cancellationToken);

        if (target.IsLive && field.FieldType != FieldType.Attachment)
        {
            await _schemaService.AddColumnForFieldAsync(target.LiveTableName, field, cancellationToken);

            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }
    }
}
