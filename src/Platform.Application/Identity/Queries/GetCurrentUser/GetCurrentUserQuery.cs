using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Queries.GetCurrentUser;

public record CurrentUserDto(Guid Id, string Email, string DisplayName, Guid? DepartmentId, IReadOnlyList<string> Roles);

public record GetCurrentUserQuery : IRequest<CurrentUserDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetCurrentUserQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null)
            throw new ForbiddenAccessException("No authenticated user.");

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);

        if (user is null)
            throw new NotFoundException(nameof(Platform.Domain.Identity.User), _currentUser.UserId);

        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var roleNames = await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(cancellationToken);

        return new CurrentUserDto(user.Id, user.Email, user.DisplayName, user.DepartmentId, roleNames);
    }
}
