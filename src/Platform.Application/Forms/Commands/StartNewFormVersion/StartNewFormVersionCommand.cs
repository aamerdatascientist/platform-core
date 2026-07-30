using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.StartNewFormVersion;

public record StartNewFormVersionCommand(Guid FormDefinitionId) : IRequest<Guid>;

public class StartNewFormVersionCommandValidator : AbstractValidator<StartNewFormVersionCommand>
{
    public StartNewFormVersionCommandValidator() => RuleFor(x => x.FormDefinitionId).NotEmpty();
}

public class StartNewFormVersionCommandHandler : IRequestHandler<StartNewFormVersionCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public StartNewFormVersionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(StartNewFormVersionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        // StartNewDraftVersion() returns the entity it creates rather than relying on
        // navigation fixup to pick it up - the established convention in this codebase
        // for exactly the reason documented in CLAUDE.md's first "known engineering
        // gotcha". Worth double-checking this one specifically, though: the new
        // FormVersion clones forward FieldDefinitions from the PREVIOUS version, and
        // those clones are a genuinely new subgraph (new GUIDs, not reached through an
        // already-tracked parent), so the usual bug shouldn't apply here - but this is
        // close enough to that bug's shape that it's worth confirming during testing,
        // not just assuming from the pattern match.
        Platform.Domain.Forms.FormVersion newVersion;
        try
        {
            newVersion = formDefinition.StartNewDraftVersion();
        }
        catch (InvalidOperationException ex)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.FormDefinitionId), ex.Message)
            });
        }

        _db.FormVersions.Add(newVersion);
        await _db.SaveChangesAsync(cancellationToken);

        return newVersion.Id;
    }
}
