using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.StageProgress;

/// <summary>
/// Brings a project's floors and stage plan in line with its setup (owner, 2026-11-10). Safe to
/// run any number of times - it only adds what is missing:
/// - floors: one per parking level (level 0, -1, ...) then Floor 1..N, each with its level
///   number and kind, in project-floors with the project lookup; floors beyond the counts are
///   set inactive (never deleted - ProjectsHook refuses the change when one has reports);
/// - the plan: when the project has no plan rows yet, a copy of every default plan row.
/// Runs after the project row is saved, so a failure here leaves the project saved; the next
/// save of the project (or <see cref="EnsureAsync"/> from the admin page) completes it.
/// </summary>
public sealed class ProjectSetup(
    IApplicationDbContext db, IDynamicDataRepository repository, StageProgressData data, ILogger<ProjectSetup> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Level numbers for the counts, bottom to top: parking 1-P, -P+2 .. 0, then 1..N.</summary>
    public static IEnumerable<int> LevelNumbers(int parkingLevels, int apartmentFloors) =>
        Enumerable.Range(0, Math.Max(0, parkingLevels)).Select(i => -i).Reverse()
            .Concat(Enumerable.Range(1, Math.Max(0, apartmentFloors)));

    public static (string Code, string LabelAr, string LabelEn, string Kind) Describe(int level, int parkingLevels) => level <= 0
        ? parkingLevels <= 1
            ? ("P1", "المواقف", "Parking", "parking")
            : ($"P{1 - level}", $"المواقف {1 - level}", $"Parking {1 - level}", "parking")
        : ($"F{level}", $"الدور {level}", $"Floor {level}", "apartment");

    public async Task EnsureAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            data.Invalidate();
            var project = await data.ProjectAsync(projectId, ct);
            if (project is null) return;
            await EnsureFloorsAsync(project, userId, ct);
            await EnsurePlanAsync(projectId, userId, ct);
            data.Invalidate();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task EnsureFloorsAsync(StageProject project, Guid userId, CancellationToken ct)
    {
        var (form, _) = await data.FormAsync(StageForms.Floors, ct);
        var fields = Writable(form);
        if (form?.TableName is null || fields is null || !Has(fields, StageForms.Floor.LevelNumber, StageForms.Floor.LevelKind)) return;

        var wanted = LevelNumbers(project.ParkingLevels, project.ApartmentFloors).ToList();
        var floors = (await data.FloorsAsync(ct)).Where(f => f.ProjectId == project.Id && f.LevelNumber is not null).ToList();
        var isActive = fields.FirstOrDefault(f => f.Code == StageForms.Floor.IsActive);

        foreach (var level in wanted)
        {
            var existing = floors.FirstOrDefault(f => f.LevelNumber == level);
            if (existing is null)
            {
                var (code, ar, en, kind) = Describe(level, project.ParkingLevels);
                await repository.InsertAsync(form.TableName, fields, new Dictionary<string, object?>
                {
                    [StageForms.Floor.Project] = project.Id,
                    [StageForms.Floor.Code] = code,
                    [StageForms.Floor.Label] = ar,
                    [StageForms.Floor.LabelEn] = en,
                    [StageForms.Floor.LevelNumber] = level,
                    [StageForms.Floor.LevelKind] = kind,
                    [StageForms.Floor.IsActive] = true,
                }, userId, ct);
            }
            else if (!existing.IsActive && isActive is not null)
                await repository.UpdateAsync(form.TableName, [isActive], existing.Id, new Dictionary<string, object?> { [isActive.Code] = true }, userId, ct);
        }
        if (isActive is not null)
            foreach (var extra in floors.Where(f => f.IsActive && !wanted.Contains(f.LevelNumber!.Value)))
                await repository.UpdateAsync(form.TableName, [isActive], extra.Id, new Dictionary<string, object?> { [isActive.Code] = false }, userId, ct);
        logger.LogInformation("Floors checked for project {ProjectId}: {Levels} levels", project.Id, wanted.Count);
    }

    private async Task EnsurePlanAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        if ((await data.ProjectPlanAsync(projectId, ct)).Count > 0) return;
        var (form, _) = await data.FormAsync(StageForms.ProjectPlan, ct);
        var fields = Writable(form);
        if (form?.TableName is null || fields is null || !Has(fields, StageForms.Plan.Project, StageForms.Plan.StageCode)) return;

        var defaults = await data.DefaultPlanAsync(ct);
        foreach (var (_, row) in defaults)
            await repository.InsertAsync(form.TableName, fields, PlanValues(row, projectId), userId, ct);
        logger.LogInformation("Copied {Count} default stage plan rows to project {ProjectId}", defaults.Count, projectId);
    }

    public static Dictionary<string, object?> PlanValues(StagePlanRow row, Guid? projectId)
    {
        var values = new Dictionary<string, object?>
        {
            [StageForms.Plan.SortOrder] = row.SortOrder,
            [StageForms.Plan.StageCode] = row.StageCode,
            [StageForms.Plan.StageNameEn] = row.StageNameEn,
            [StageForms.Plan.StageNameAr] = row.StageNameAr,
            [StageForms.Plan.SubstepCode] = row.SubstepCode,
            [StageForms.Plan.SubstepNameEn] = row.SubstepNameEn,
            [StageForms.Plan.SubstepNameAr] = row.SubstepNameAr,
            [StageForms.Plan.Days] = row.Days,
            [StageForms.Plan.DurationType] = StagePlanValues.ToValue(row.DurationType),
            [StageForms.Plan.LevelScope] = StagePlanValues.ToValue(row.LevelScope),
            [StageForms.Plan.AppliesWhen] = StagePlanValues.ToValue(row.AppliesWhen),
            [StageForms.Plan.IsActive] = row.IsActive,
        };
        if (projectId is { } id) values[StageForms.Plan.Project] = id;
        return values;
    }

    private static List<FieldDefinition>? Writable(FormDefinition? form) =>
        form?.GetPublishedVersion()?.Fields.Where(f => f.IsActive && f.SectionId is null && string.IsNullOrWhiteSpace(f.Formula)).ToList();

    private static bool Has(IEnumerable<FieldDefinition> fields, params string[] codes) => codes.All(c => fields.Any(f => f.Code == c));
}
