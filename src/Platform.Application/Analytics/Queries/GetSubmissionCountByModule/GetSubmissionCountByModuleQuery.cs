using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Dtos;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetSubmissionCountByModule;

public record GetSubmissionCountByModuleQuery : IRequest<IReadOnlyList<ModuleSubmissionCountDto>>;

public class GetSubmissionCountByModuleQueryHandler
    : IRequestHandler<GetSubmissionCountByModuleQuery, IReadOnlyList<ModuleSubmissionCountDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public GetSubmissionCountByModuleQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<IReadOnlyList<ModuleSubmissionCountDto>> Handle(
        GetSubmissionCountByModuleQuery request, CancellationToken cancellationToken)
    {
        // Only published forms have a TableName - a draft-only form has no Data_* table to
        // count rows in yet.
        var publishedForms = await _db.FormDefinitions
            .Where(f => f.TableName != null)
            .Select(f => new { f.Id, f.Code, f.Name, f.ModuleName, f.TableName })
            .ToListAsync(cancellationToken);

        // One COUNT(1) per form's Data_* table - these tables aren't part of EF's model, so
        // there's no single GROUP BY query that can span all of them at once (see
        // DynamicDataRepository's own doc comment on why dynamic tables are handled
        // separately from IApplicationDbContext). Form counts here are small enough that a
        // handful of round trips is the right amount of complexity for Phase 1 - a UNION ALL
        // built by hand over N dynamic table names would be more fragile for no real gain
        // at this volume.
        var formCounts = new List<(string ModuleName, FormSubmissionCountDto Dto)>();
        foreach (var form in publishedForms)
        {
            var count = await _dynamicDataRepository.CountAsync(form.TableName!, cancellationToken);
            formCounts.Add((form.ModuleName, new FormSubmissionCountDto(form.Id, form.Code, form.Name, count)));
        }

        return formCounts
            .GroupBy(x => x.ModuleName)
            .Select(g => new ModuleSubmissionCountDto(
                g.Key,
                g.Select(x => x.Dto).OrderByDescending(d => d.SubmissionCount).ToList()))
            .OrderBy(m => m.ModuleName)
            .ToList();
    }
}
