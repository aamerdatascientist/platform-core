using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Queries.GetRoles;

public record RoleDto(Guid Id, string Name, string? Description, bool IsSystemRole);

public record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IApplicationDbContext _db;

    public GetRolesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken) =>
        await _db.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystemRole))
            .ToListAsync(cancellationToken);
}
