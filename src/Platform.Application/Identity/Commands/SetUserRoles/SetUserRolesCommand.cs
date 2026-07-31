using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.SetUserRoles;

public record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest;

public class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public class SetUserRolesCommandHandler : IRequestHandler<SetUserRolesCommand>
{
    private readonly IApplicationDbContext _db;

    public SetUserRolesCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(SetUserRolesCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.Include(u => u.UserRoles).SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);

        var requestedRoleIds = request.RoleIds.Distinct().ToList();
        var validRoleIds = await _db.Roles.Where(r => requestedRoleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken);
        if (validRoleIds.Count != requestedRoleIds.Count)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.RoleIds), "One or more role IDs don't exist.")
            });

        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var toAdd = requestedRoleIds.Except(currentRoleIds).ToList();
        var toRemove = currentRoleIds.Except(requestedRoleIds).ToList();

        // The EF tracking gotcha (see CLAUDE.md) applies here too, in a variant worth
        // naming precisely: UserRole's key isn't one client-generated GUID, it's a
        // composite of two real, already-existing GUIDs (UserId + RoleId) - which is if
        // anything an even easier way to fool EF's new-vs-existing heuristic into assuming
        // a row this "real-looking" must already exist. AssignRole() returns the entity it
        // creates for exactly this reason - explicit Add() on the DbSet, not reliance on
        // navigation fixup from the already-tracked User.
        foreach (var roleId in toAdd)
        {
            var newUserRole = user.AssignRole(roleId);
            if (newUserRole is not null) _db.UserRoles.Add(newUserRole);
        }

        // Removal is the safe direction by comparison - these rows are already tracked
        // (loaded via Include above), so removing them from the collection is a normal,
        // correctly-handled EF scenario, not the same trap as adding new ones.
        foreach (var roleId in toRemove)
            user.RemoveRole(roleId);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
