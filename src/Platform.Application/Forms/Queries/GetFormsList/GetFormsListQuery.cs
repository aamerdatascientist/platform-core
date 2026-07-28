using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetFormsList;

/// <summary>Lightweight on purpose - this drives a nav list, not a detail view. Full field
/// definitions come from GetFormDefinitionQuery once a specific form is selected.</summary>
public record FormSummaryDto(Guid Id, string Code, string Name, string ModuleName, FormStatus Status);

public record GetFormsListQuery(string? ModuleName = null) : IRequest<IReadOnlyList<FormSummaryDto>>;

public class GetFormsListQueryHandler : IRequestHandler<GetFormsListQuery, IReadOnlyList<FormSummaryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetFormsListQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FormSummaryDto>> Handle(GetFormsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.FormDefinitions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
            query = query.Where(f => f.ModuleName == request.ModuleName);

        return await query
            .OrderBy(f => f.ModuleName).ThenBy(f => f.Name)
            .Select(f => new FormSummaryDto(f.Id, f.Code, f.Name, f.ModuleName, f.Status))
            .ToListAsync(cancellationToken);
    }
}
