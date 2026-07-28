using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.RemoveField;

public record RemoveFieldCommand(Guid FormDefinitionId, Guid FieldDefinitionId) : IRequest;

public class RemoveFieldCommandValidator : AbstractValidator<RemoveFieldCommand>
{
    public RemoveFieldCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class RemoveFieldCommandHandler : IRequestHandler<RemoveFieldCommand>
{
    private readonly IApplicationDbContext _db;

    public RemoveFieldCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(RemoveFieldCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var draft = formDefinition.GetDraftVersion();
        draft.RemoveField(request.FieldDefinitionId);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
