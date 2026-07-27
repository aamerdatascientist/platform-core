using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

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
        await _schemaService.RefreshReportingViewAsync(formDefinition, draft, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new PublishFormVersionResult(draft.Id, draft.VersionNumber, tableName);
    }
}
