using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.SetFormAllowedUsers;

public record SetFormAllowedUsersCommand(Guid FormDefinitionId, IReadOnlyList<Guid> UserIds) : IRequest;

public class SetFormAllowedUsersCommandValidator : AbstractValidator<SetFormAllowedUsersCommand>
{
    public SetFormAllowedUsersCommandValidator() => RuleFor(x => x.FormDefinitionId).NotEmpty();
}

public class SetFormAllowedUsersCommandHandler : IRequestHandler<SetFormAllowedUsersCommand>
{
    private readonly IApplicationDbContext _db;

    public SetFormAllowedUsersCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(SetFormAllowedUsersCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.AllowedUsers)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var requestedUserIds = request.UserIds.Distinct().ToList();
        if (requestedUserIds.Count > 0)
        {
            var validUserIds = await _db.Users.Where(u => requestedUserIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
            if (validUserIds.Count != requestedUserIds.Count)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(nameof(request.UserIds), "One or more user IDs don't exist.")
                });
        }

        // Same tracking-gotcha shape as FormDefinitionRole/UserRole - explicit Add() on
        // the newly created rows only, not reliance on navigation fixup from the
        // already-tracked FormDefinition. See CLAUDE.md.
        var newlyAdded = formDefinition.SetAllowedUsers(requestedUserIds);
        foreach (var entry in newlyAdded)
            _db.FormDefinitionUsers.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
