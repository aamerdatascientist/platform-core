using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetProjectProgress;

public record GetProjectProgressQuery : IRequest<IReadOnlyList<ProjectProgressDto>>;

public record ProjectProgressDto(
    Guid ProjectId, string ProjectCode, string ProjectName, string? Status,
    DateTime? StartDateUtc, DateTime? ExpectedCompletionUtc, decimal? PercentComplete);

public class GetProjectProgressQueryHandler : IRequestHandler<GetProjectProgressQuery, IReadOnlyList<ProjectProgressDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public GetProjectProgressQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<IReadOnlyList<ProjectProgressDto>> Handle(GetProjectProgressQuery request, CancellationToken cancellationToken)
    {
        var projectsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "projects", cancellationToken);
        if (projectsForm is null) return Array.Empty<ProjectProgressDto>();

        var activeFields = projectsForm.GetPublishedVersion()!.Fields.Where(f => f.IsActive).ToList();
        var page = await _dynamicDataRepository.QueryAsync(projectsForm.TableName!, activeFields, 1, 500, cancellationToken);

        var nowUtc = DateTime.UtcNow;

        return page.Items.Select(row =>
        {
            var startDate = ParseUtcDateTime(row.Values.GetValueOrDefault("start_date"));
            var expectedCompletion = ParseUtcDateTime(row.Values.GetValueOrDefault("expected_completion"));

            return new ProjectProgressDto(
                row.Id,
                row.Values.GetValueOrDefault("project_code")?.ToString() ?? string.Empty,
                row.Values.GetValueOrDefault("project_name")?.ToString() ?? string.Empty,
                row.Values.GetValueOrDefault("status")?.ToString(),
                startDate,
                expectedCompletion,
                ComputePercentComplete(startDate, expectedCompletion, nowUtc));
        }).ToList();
    }

    /// <summary>
    /// Both dates are optional on Projects (see the Phase 0 audit) - null start or end
    /// means "not enough information", which the frontend renders as "—", not 0%/100%.
    /// Nothing validates expected_completion >= start_date either, so a zero-or-negative
    /// span (bad data entry) is treated the same way - unavailable, not a divide-by-zero or
    /// a nonsense negative/inverted percentage.
    /// </summary>
    public static decimal? ComputePercentComplete(DateTime? startDateUtc, DateTime? expectedCompletionUtc, DateTime nowUtc)
    {
        if (startDateUtc is not { } start || expectedCompletionUtc is not { } end) return null;

        var totalSpan = end - start;
        if (totalSpan <= TimeSpan.Zero) return null;

        var elapsed = nowUtc - start;
        var rawPercent = (decimal)(elapsed.TotalSeconds / totalSpan.TotalSeconds) * 100m;
        return Math.Clamp(rawPercent, 0m, 100m);
    }

    /// <summary>
    /// DynamicRow's values are loosely-typed objects straight from Dapper - Npgsql's default
    /// CLR mapping for timestamptz can surface as either DateTime or DateTimeOffset
    /// depending on driver configuration, so this normalizes both to a UTC DateTime rather
    /// than assuming one.
    /// </summary>
    public static DateTime? ParseUtcDateTime(object? raw) => raw switch
    {
        null => null,
        DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        _ => null
    };
}
