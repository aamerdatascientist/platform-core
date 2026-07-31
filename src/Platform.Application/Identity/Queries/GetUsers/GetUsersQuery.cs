using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Queries.GetUsers;

public record UserRoleSummary(Guid Id, string Name);

public record UserSummaryDto(Guid Id, string Email, string DisplayName, bool IsActive, IReadOnlyList<UserRoleSummary> Roles);

public record GetUsersQuery : IRequest<IReadOnlyList<UserSummaryDto>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<UserSummaryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetUsersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserSummaryDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _db.Users.Include(u => u.UserRoles).OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var rolesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return users
            .Select(u => new UserSummaryDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.UserRoles
                    .Where(ur => rolesById.ContainsKey(ur.RoleId))
                    .Select(ur => new UserRoleSummary(ur.RoleId, rolesById[ur.RoleId]))
                    .ToList()))
            .ToList();
    }
}
