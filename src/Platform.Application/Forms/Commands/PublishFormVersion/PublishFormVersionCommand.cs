using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.PublishFormVersion;

public record PublishFormVersionCommand(Guid FormDefinitionId) : IRequest<PublishFormVersionResult>;

public record PublishFormVersionResult(Guid FormVersionId, int VersionNumber, string TableName);

public class PublishFormVersionCommandValidator : AbstractValidator<PublishFormVersionCommand>
{
    public PublishFormVersionCommandValidator() => RuleFor(x => x.FormDefinitionId).NotEmpty();
}

public class PublishFormVersionCommandHandler : IRequestHandler<PublishFormVersionCommand, PublishFormVersionResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public PublishFormVersionCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task<PublishFormVersionResult> Handle(
        PublishFormVersionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var draft = formDefinition.GetDraftVersion();

        // Domain validation (has active fields, correct status) happens first and is cheap.
        // Only once that passes do we touch the database schema - DDL is expensive to
        // execute and awkward to roll back, so we never want to reach it with a request
        // that was going to fail anyway.
        draft.MarkPublished();

        var tableName = await _schemaService.EnsureTableForPublishedVersionAsync(formDefinition, draft, cancellationToken);
        formDefinition.MarkPublished(draft, tableName);

        var lookupTargets = await LoadLookupTargetsAsync(draft, cancellationToken);
        await _schemaService.RefreshReportingViewAsync(formDefinition, draft, lookupTargets, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new PublishFormVersionResult(draft.Id, draft.VersionNumber, tableName);
    }

    /// <summary>
    /// Loads the FormDefinitions referenced by this version's Lookup fields, with Versions and
    /// Fields included, so RefreshReportingViewAsync can resolve each Lookup to a readable
    /// display field without issuing its own queries against the EF-owned static schema.
    /// </summary>
    private async Task<Dictionary<Guid, FormDefinition>> LoadLookupTargetsAsync(
        FormVersion draft, CancellationToken cancellationToken)
    {
        var targetIds = draft.Fields
            .Where(f => f.IsActive && f.FieldType == FieldType.Lookup)
            .Select(f => f.LookupFormDefinitionId!.Value)
            .Distinct()
            .ToList();

        if (targetIds.Count == 0) return new Dictionary<Guid, FormDefinition>();

        var targets = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Where(f => targetIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        return targets.ToDictionary(f => f.Id);
    }
}
