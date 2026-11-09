using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.StageProgress;

/// <summary>A project as the stage module sees it.</summary>
public sealed record StageProject(
    Guid Id, string Code, string Name, string? Status, DateOnly? StartDate,
    int ParkingLevels, int ApartmentFloors, ProjectShape Shape);

/// <summary>A floor row of project-floors (level number null on rows made before 2026-11-10).</summary>
public sealed record StageFloorRow(Guid Id, Guid ProjectId, string? Code, string LabelAr, string? LabelEn, int? LevelNumber, LevelKind? Kind, bool IsActive);

/// <summary>One stage block with its report's header (project and date).</summary>
public sealed record StageBlockRow(Guid RecordId, Guid ProjectId, StageReportBlock Block);

/// <summary>Everything the calculations need about one project.</summary>
public sealed record ProjectStageData(
    StageProject Project,
    IReadOnlyList<StagePlanRow> Plan,
    IReadOnlyList<StageBlockRow> Blocks,
    IReadOnlyList<StageBaseline> Baselines,
    IReadOnlyList<StageReopening> Reopenings,
    IReadOnlyList<PauseEvent> Pauses);

/// <summary>
/// Reads the stage forms (projects, floors, plans, daily-report blocks, pause log) into plain
/// records. Scoped: each form is read at most once per request, so options providers called
/// many times in one request (a records page) stay cheap.
/// </summary>
public sealed class StageProgressData(IApplicationDbContext db, IDynamicDataRepository repository)
{
    private const int PageSize = 200;
    private readonly Dictionary<string, (FormDefinition? Form, IReadOnlyList<DynamicRow> Rows)> _forms = new();
    private IReadOnlyList<StageBlockRow>? _blocks;

    public async Task<(FormDefinition? Form, IReadOnlyList<DynamicRow> Rows)> FormAsync(string code, CancellationToken ct)
    {
        if (_forms.TryGetValue(code, out var cached)) return cached;
        var form = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .SingleOrDefaultAsync(f => f.Code == code, ct);
        var rows = new List<DynamicRow>();
        if (form?.TableName is not null && form.GetPublishedVersion() is { } version)
        {
            var fields = version.Fields.Where(f => f.IsActive && f.SectionId is null).ToList();
            for (var page = 1; ; page++)
            {
                var result = await repository.QueryAsync(form.TableName, fields, page, PageSize, cancellationToken: ct);
                rows.AddRange(result.Items);
                if (result.Items.Count == 0 || rows.Count >= result.TotalCount) break;
            }
        }
        return _forms[code] = (form, rows);
    }

    /// <summary>Forgets what was read (after this request wrote to a form).</summary>
    public void Invalidate(string? code = null)
    {
        if (code is null) _forms.Clear(); else _forms.Remove(code);
        _blocks = null;
    }

    public async Task<IReadOnlyList<StageProject>> ProjectsAsync(CancellationToken ct)
    {
        var (_, rows) = await FormAsync(StageForms.Projects, ct);
        var floors = await FloorsAsync(ct);
        return rows.Select(r => ToProject(r, floors.Where(f => f.ProjectId == r.Id).ToList())).ToList();
    }

    public async Task<StageProject?> ProjectAsync(Guid projectId, CancellationToken ct) =>
        (await ProjectsAsync(ct)).FirstOrDefault(p => p.Id == projectId);

    public async Task<IReadOnlyList<StageFloorRow>> FloorsAsync(CancellationToken ct)
    {
        var (_, rows) = await FormAsync(StageForms.Floors, ct);
        return rows.Select(r => new StageFloorRow(
            r.Id,
            Id(r, StageForms.Floor.Project) ?? Guid.Empty,
            Text(r, StageForms.Floor.Code),
            Text(r, StageForms.Floor.Label) ?? Text(r, StageForms.Floor.Code) ?? string.Empty,
            Text(r, StageForms.Floor.LabelEn),
            Int(r, StageForms.Floor.LevelNumber),
            Text(r, StageForms.Floor.LevelKind) switch { "parking" => LevelKind.Parking, "apartment" => LevelKind.Apartment, _ => null },
            Bool(r, StageForms.Floor.IsActive) ?? true)).ToList();
    }

    private static StageProject ToProject(DynamicRow r, IReadOnlyList<StageFloorRow> floors)
    {
        // Levels the calculations use: floors with a level number and kind (rows made before
        // the stage model have neither until they are mapped).
        var levels = floors
            .Where(f => f.LevelNumber is not null && f.Kind is not null)
            .Select(f => new ProjectLevel(f.Id, f.LevelNumber!.Value, f.Kind!.Value, f.LabelAr, f.LabelEn, f.IsActive))
            .ToList();
        var shape = new ProjectShape(
            levels,
            Int(r, StageForms.Project.OutsideSides) ?? 4,
            Bool(r, StageForms.Project.NeedsDeconstruction) ?? false,
            Bool(r, StageForms.Project.NeedsSupport) ?? false);
        return new StageProject(
            r.Id,
            Text(r, StageForms.Project.Code) ?? string.Empty,
            Text(r, StageForms.Project.Name) ?? string.Empty,
            Text(r, StageForms.Project.Status),
            LocalDate(r, StageForms.Project.StartDate),
            Int(r, StageForms.Project.ParkingLevels) ?? 0,
            Int(r, StageForms.Project.ApartmentFloors) ?? 0,
            shape);
    }

    public async Task<IReadOnlyList<StagePlanRow>> ProjectPlanAsync(Guid projectId, CancellationToken ct)
    {
        var (_, rows) = await FormAsync(StageForms.ProjectPlan, ct);
        return rows.Where(r => Id(r, StageForms.Plan.Project) == projectId).Select(ToPlanRow).OrderBy(r => r.SortOrder).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, StagePlanRow Row)>> DefaultPlanAsync(CancellationToken ct)
    {
        var (_, rows) = await FormAsync(StageForms.DefaultPlan, ct);
        return rows.Select(r => (r.Id, ToPlanRow(r))).OrderBy(x => x.Item2.SortOrder).ToList();
    }

    public static StagePlanRow ToPlanRow(DynamicRow r) => new(
        Int(r, StageForms.Plan.SortOrder) ?? 0,
        Text(r, StageForms.Plan.StageCode) ?? string.Empty,
        Text(r, StageForms.Plan.StageNameEn) ?? Text(r, StageForms.Plan.StageNameAr) ?? string.Empty,
        Text(r, StageForms.Plan.StageNameAr) ?? Text(r, StageForms.Plan.StageNameEn) ?? string.Empty,
        Text(r, StageForms.Plan.SubstepCode),
        Text(r, StageForms.Plan.SubstepNameEn),
        Text(r, StageForms.Plan.SubstepNameAr),
        Int(r, StageForms.Plan.Days) ?? 0,
        StagePlanValues.ParseDurationType(Text(r, StageForms.Plan.DurationType)),
        StagePlanValues.ParseLevelScope(Text(r, StageForms.Plan.LevelScope)),
        StagePlanValues.ParseAppliesWhen(Text(r, StageForms.Plan.AppliesWhen)),
        Bool(r, StageForms.Plan.IsActive) ?? true);

    /// <summary>Every live stage block of the daily report (v2 reports; v1 reports have none).</summary>
    public async Task<IReadOnlyList<StageBlockRow>> BlocksAsync(CancellationToken ct)
    {
        if (_blocks is not null) return _blocks;
        var (form, parents) = await FormAsync(StageForms.DailyReport, ct);
        var version = form?.GetPublishedVersion();
        var section = version?.Sections.FirstOrDefault(s => s.Code == StageForms.StagesSection);
        if (form?.TableName is null || version is null || section?.ChildTableName is null) return _blocks = [];

        var fields = version.Fields.Where(f => f.IsActive && f.SectionId == section.Id).ToList();
        var rows = await repository.GetAllBlocksAsync(section.ChildTableName, form.TableName, fields, ct);
        var parentById = parents.ToDictionary(p => p.Id);
        var result = new List<StageBlockRow>();
        foreach (var b in rows)
        {
            if (!parentById.TryGetValue(b.ParentId, out var parent)) continue;
            if (Id(parent, StageForms.Report.Project) is not { } projectId || LocalDate(parent, StageForms.Report.Date) is not { } date) continue;
            if (BlockText(b, StageForms.Report.Stage) is not { } stage) continue;
            var levels = new List<Guid>();
            if (b.Values.GetValueOrDefault(StageForms.Report.Level) is Guid level) levels.Add(level);
            if (b.Values.GetValueOrDefault(StageForms.Report.Floors) is IEnumerable<Guid> many) levels.AddRange(many);
            var sides = (b.Values.GetValueOrDefault(StageForms.Report.Sides) as IEnumerable<string> ?? [])
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).ToList();
            result.Add(new StageBlockRow(b.ParentId, projectId, new StageReportBlock(
                b.ParentId, date, stage, BlockText(b, StageForms.Report.Substep), levels.Distinct().ToList(), sides,
                b.Values.GetValueOrDefault(StageForms.Report.PlanMet) is true,
                b.Values.GetValueOrDefault(StageForms.Report.Finished) is true)));
        }
        return _blocks = result;
    }

    public async Task<IReadOnlyList<PauseEvent>> PausesAsync(Guid projectId, CancellationToken ct)
    {
        var (_, rows) = await FormAsync(StageForms.PauseLog, ct);
        return rows
            .Where(r => Id(r, StageForms.Pause.Project) == projectId && LocalDate(r, StageForms.Pause.Date) is not null)
            .Select(r => new PauseEvent(LocalDate(r, StageForms.Pause.Date)!.Value,
                Text(r, StageForms.Pause.Action) == "resume" ? PauseAction.Resume : PauseAction.Pause))
            .ToList();
    }

    public async Task<ProjectStageData?> ProjectDataAsync(Guid projectId, CancellationToken ct)
    {
        var project = await ProjectAsync(projectId, ct);
        if (project is null) return null;
        var plan = await ProjectPlanAsync(projectId, ct);
        var blocks = (await BlocksAsync(ct)).Where(b => b.ProjectId == projectId).ToList();
        var baselines = await db.StageBaselines.AsNoTracking().Where(b => b.ProjectId == projectId).ToListAsync(ct);
        var reopenings = await db.StageUnitReopenings.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync(ct);
        return new ProjectStageData(
            project, plan, blocks,
            baselines.Select(b => new StageBaseline(b.StageCode, b.SubstepCode, b.LevelId, b.Side, b.EarnedDays, b.Finished)).ToList(),
            reopenings.Select(r => new StageReopening(UnitKeys.For(r.StageCode, r.SubstepCode, r.LevelId, r.Side), r.ReopenedOn)).ToList(),
            await PausesAsync(projectId, ct));
    }

    // ------------------------------------------------------------------ value readers

    public static string? Text(DynamicRow row, string code) =>
        row.Values.GetValueOrDefault(code)?.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static string? BlockText(BlockRow row, string code) =>
        row.Values.GetValueOrDefault(code)?.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    public static Guid? Id(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var parsed) => parsed,
        _ => null,
    };

    public static int? Int(DynamicRow row, string code)
    {
        try { return row.Values.GetValueOrDefault(code) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null; }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    public static bool? Bool(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => null,
    };

    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    public static DateOnly? LocalDate(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) switch
    {
        DateTime dateTime => DateOnly.FromDateTime((dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime) + SaudiArabiaOffset),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime + SaudiArabiaOffset),
        _ => null,
    };

    public static DateOnly SaudiToday() => DateOnly.FromDateTime(DateTime.UtcNow + SaudiArabiaOffset);
}
