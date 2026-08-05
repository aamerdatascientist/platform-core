using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetFormsList;

/// <summary>Lightweight on purpose - this drives a nav list, not a detail view. Full field
/// definitions come from GetFormDefinitionQuery once a specific form is selected.</summary>
public record FormSummaryDto(Guid Id, string Code, string Name, string ModuleName, FormStatus Status);

public record GetFormsListQuery(string? ModuleName = null) : IRequest<IReadOnlyList<FormSummaryDto>>;

public class GetFormsListQueryHandler : IRequestHandler<GetFormsListQuery, IReadOnlyList<FormSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetFormsListQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<FormSummaryDto>> Handle(GetFormsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.FormDefinitions.Include(f => f.AllowedRoles).Include(f => f.AllowedUsers).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
            query = query.Where(f => f.ModuleName == request.ModuleName);

        var forms = await query.OrderBy(f => f.ModuleName).ThenBy(f => f.Name).ToListAsync(cancellationToken);
        var roleNamesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return forms
            .Where(f =>
            {
                var allowedNames = f.AllowedRoles.Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId)).Where(n => n is not null).Select(n => n!).ToList();
                return FormAccessChecker.HasAccess(
                    allowedNames,
                    f.AllowedUsers.Select(au => au.UserId).ToList(),
                    _currentUser.Roles,
                    _currentUser.UserId);
            })
            .Select(f => new FormSummaryDto(f.Id, f.Code, f.Name, f.ModuleName, f.Status))
            .ToList();
    }
}
