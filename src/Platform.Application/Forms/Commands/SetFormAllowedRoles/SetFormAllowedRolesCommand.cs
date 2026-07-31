using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.SetFormAllowedRoles;

public record SetFormAllowedRolesCommand(Guid FormDefinitionId, IReadOnlyList<Guid> RoleIds) : IRequest;

public class SetFormAllowedRolesCommandValidator : AbstractValidator<SetFormAllowedRolesCommand>
{
    public SetFormAllowedRolesCommandValidator() => RuleFor(x => x.FormDefinitionId).NotEmpty();
}

public class SetFormAllowedRolesCommandHandler : IRequestHandler<SetFormAllowedRolesCommand>
{
    private readonly IApplicationDbContext _db;

    public SetFormAllowedRolesCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(SetFormAllowedRolesCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.AllowedRoles)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var requestedRoleIds = request.RoleIds.Distinct().ToList();
        if (requestedRoleIds.Count > 0)
        {
            var validRoleIds = await _db.Roles.Where(r => requestedRoleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken);
            if (validRoleIds.Count != requestedRoleIds.Count)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(nameof(request.RoleIds), "One or more role IDs don't exist.")
                });
        }

        // Same shape of EF tracking gotcha as UserRole (see CLAUDE.md and the
        // user-management round) - SetAllowedRoles returns the rows it actually creates,
        // and only those get an explicit Add() here. Rows it removes are already tracked
        // (loaded via Include above), so that direction doesn't need special handling.
        var newlyAdded = formDefinition.SetAllowedRoles(requestedRoleIds);
        foreach (var entry in newlyAdded)
            _db.FormDefinitionRoles.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
