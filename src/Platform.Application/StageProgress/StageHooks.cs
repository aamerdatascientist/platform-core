using System.Globalization;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;
using Platform.Domain.Forms;
using AppValidation = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Application.StageProgress;

internal static class HookSupport
{
    public static Guid? Guid(IReadOnlyDictionary<string, object?> values, string code) => values.GetValueOrDefault(code) switch
    {
        System.Guid g => g,
        _ => SubmissionPreparer.AsText(values).GetValueOrDefault(code) is { } text && System.Guid.TryParse(text, out var parsed) ? parsed : null,
    };

    public static int? Int(IReadOnlyDictionary<string, object?> values, string code) =>
        values.GetValueOrDefault(code) switch
        {
            int i => i,
            decimal d => (int)d,
            _ => int.TryParse(SubmissionPreparer.AsText(values).GetValueOrDefault(code), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
        };

    /// <summary>A form date as the Saudi calendar day it names.</summary>
    public static DateOnly? Date(IReadOnlyDictionary<string, object?> values, string code)
    {
        var text = SubmissionPreparer.AsText(values).GetValueOrDefault(code);
        if (text is null || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return null;
        var local = parsed.Kind switch
        {
            DateTimeKind.Utc => parsed.AddHours(3),
            DateTimeKind.Local => parsed.ToUniversalTime().AddHours(3),
            _ => parsed,
        };
        return DateOnly.FromDateTime(local);
    }

    public static AppValidation Refuse(string code, string field, string message) => new(code, [new ValidationFailure(field, message)]);
}

/// <summary>
/// projects: a new project gets its floors and its own copy of the default stage plan; a
/// change in floor counts adds or retires floors - refused when it would retire a floor that
/// a report or the starting position names (owner, 2026-11-10: block the reduction).
/// </summary>
public sealed class ProjectsHook(StageProgressData data, ProjectSetup setup) : IFormSubmissionHook
{
    public const string FloorReduction = "stage.floorReduction";

    public string FormCode => StageForms.Projects;

    public string? LockKey(SubmissionHookContext context) => "stage-projects";

    public async Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken)
    {
        if (context.RecordId is not { } projectId) return;
        data.Invalidate();
        var header = context.Submission.Header;
        var parking = HookSupport.Int(header, StageForms.Project.ParkingLevels) ?? 0;
        var apartments = HookSupport.Int(header, StageForms.Project.ApartmentFloors) ?? 0;
        var wanted = ProjectSetup.LevelNumbers(parking, apartments).ToHashSet();
        var floors = (await data.FloorsAsync(cancellationToken)).Where(f => f.ProjectId == projectId && f.IsActive && f.LevelNumber is not null).ToList();
        var leaving = floors.Where(f => !wanted.Contains(f.LevelNumber!.Value)).Select(f => f.Id).ToHashSet();
        if (leaving.Count == 0) return;

        var used = (await data.BlocksAsync(cancellationToken)).Any(b => b.ProjectId == projectId && b.Block.LevelIds.Any(leaving.Contains))
                   || (await data.ProjectDataAsync(projectId, cancellationToken))?.Baselines.Any(b => b.LevelId is { } l && leaving.Contains(l)) == true;
        if (used)
            throw HookSupport.Refuse(FloorReduction, StageForms.Project.ApartmentFloors,
                "A floor that would be removed already has reports, so the floor count can't be reduced.");
    }

    public Task AfterSaveAsync(SubmissionHookContext context, Guid recordId, CancellationToken cancellationToken) =>
        setup.EnsureAsync(recordId, context.UserId, cancellationToken);
}

/// <summary>
/// daily-progress-report (v2, with stage blocks): the project must be in Planning or Active
/// (owner, 2026-11-10; On hold or Completed refuse), and every block must pass
/// <see cref="StageEligibility"/>. Runs under the project's lock, so two foremen reporting the
/// same project are checked one after the other.
/// </summary>
public sealed class DailyReportHook(StageProgressData data) : IFormSubmissionHook
{
    public const string ProjectPaused = "stage.projectPaused";
    public const string ProjectClosed = "stage.projectClosed";

    public string FormCode => StageForms.DailyReport;

    public string? LockKey(SubmissionHookContext context) =>
        HookSupport.Guid(context.Submission.Header, StageForms.Report.Project) is { } project ? $"stage-project:{project}" : null;

    public async Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken)
    {
        var header = context.Submission.Header;
        var blocks = context.Submission.BlocksOf(StageForms.StagesSection);
        if (HookSupport.Guid(header, StageForms.Report.Project) is not { } projectId) return;

        data.Invalidate(); // read what other reports committed before this lock was taken
        var project = await data.ProjectDataAsync(projectId, cancellationToken);
        if (project is null) return;

        var status = project.Project.Status;
        if (status == StageForms.Status.OnHold || PauseSpans.IsPaused(project.Pauses))
            throw HookSupport.Refuse(ProjectPaused, StageForms.Report.Project, "This project is paused, so reports can't be sent until it is resumed.");
        if (status is StageForms.Status.Completed or StageForms.Status.Cancelled)
            throw HookSupport.Refuse(ProjectClosed, StageForms.Report.Project, "This project is closed, so reports can't be sent.");

        // v1 reports (no stage blocks) still go through - only stage blocks have rules.
        if (blocks.Count == 0 || HookSupport.Date(header, StageForms.Report.Date) is not { } date) return;

        var schedule = StageScheduleBuilder.Build(project.Plan, project.Project.Shape);
        var others = project.Blocks.Where(b => b.RecordId != context.RecordId).Select(b => b.Block).ToList();
        var existing = StageProgressCalculator.Compute(schedule.Units, others, project.Baselines, project.Reopenings);
        var incoming = blocks.Select(b => ToBlock(b, date)).ToList();
        var problems = StageEligibility.Check(schedule, incoming, existing);
        if (problems.Count == 0) return;

        var code = problems[0].Code;
        throw new AppValidation(code, problems.Where(p => p.Code == code)
            .Select(p => new ValidationFailure($"{StageForms.StagesSection}[{p.BlockIndex}].{p.Field}", p.Message)));
    }

    public static StageReportBlock ToBlock(IReadOnlyDictionary<string, object?> b, DateOnly date)
    {
        var text = SubmissionPreparer.AsText(b);
        var levels = new List<Guid>();
        if (b.GetValueOrDefault(StageForms.Report.Level) is Guid level) levels.Add(level);
        if (b.GetValueOrDefault(StageForms.Report.Floors) is IEnumerable<Guid> floors) levels.AddRange(floors);
        var sides = (b.GetValueOrDefault(StageForms.Report.Sides) as IEnumerable<string> ?? [])
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).ToList();
        return new StageReportBlock(Guid.Empty, date, text.GetValueOrDefault(StageForms.Report.Stage) ?? string.Empty,
            string.IsNullOrWhiteSpace(text.GetValueOrDefault(StageForms.Report.Substep)) ? null : text[StageForms.Report.Substep],
            levels.Distinct().ToList(), sides,
            b.GetValueOrDefault(StageForms.Report.PlanMet) is true || text.GetValueOrDefault(StageForms.Report.PlanMet) == "true",
            b.GetValueOrDefault(StageForms.Report.Finished) is true || text.GetValueOrDefault(StageForms.Report.Finished) == "true");
    }
}

/// <summary>
/// project-pause-log: administrators only (owner, 2026-11-10). Pause is refused on a paused
/// project and Resume on one that isn't paused; saving sets the project's status to On hold
/// or back to Active.
/// </summary>
public sealed class PauseLogHook(StageProgressData data, IDynamicDataRepository repository, ILogger<PauseLogHook> logger) : IFormSubmissionHook
{
    public const string AdminOnly = "stage.pauseAdminOnly";
    public const string AlreadyPaused = "stage.alreadyPaused";
    public const string NotPaused = "stage.notPaused";

    public string FormCode => StageForms.PauseLog;

    public string? LockKey(SubmissionHookContext context) =>
        HookSupport.Guid(context.Submission.Header, StageForms.Pause.Project) is { } project ? $"stage-project:{project}" : null;

    public async Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken)
    {
        if (!context.IsAdministrator)
            throw HookSupport.Refuse(AdminOnly, StageForms.Pause.Action, "Only an administrator can pause or resume a project.");
        if (context.RecordId is not null) return; // correcting an old entry: no state rule
        if (HookSupport.Guid(context.Submission.Header, StageForms.Pause.Project) is not { } projectId) return;

        data.Invalidate();
        var project = await data.ProjectAsync(projectId, cancellationToken);
        var paused = PauseSpans.IsPaused(await data.PausesAsync(projectId, cancellationToken)) || project?.Status == StageForms.Status.OnHold;
        var action = SubmissionPreparer.AsText(context.Submission.Header).GetValueOrDefault(StageForms.Pause.Action);
        if (action == "pause" && paused)
            throw HookSupport.Refuse(AlreadyPaused, StageForms.Pause.Action, "This project is already paused.");
        if (action == "resume" && !paused)
            throw HookSupport.Refuse(NotPaused, StageForms.Pause.Action, "This project isn't paused.");
    }

    public async Task AfterSaveAsync(SubmissionHookContext context, Guid recordId, CancellationToken cancellationToken)
    {
        if (context.RecordId is not null) return;
        if (HookSupport.Guid(context.Submission.Header, StageForms.Pause.Project) is not { } projectId) return;
        var action = SubmissionPreparer.AsText(context.Submission.Header).GetValueOrDefault(StageForms.Pause.Action);
        var (form, _) = await data.FormAsync(StageForms.Projects, cancellationToken);
        var status = form?.GetPublishedVersion()?.Fields.FirstOrDefault(f => f.IsActive && f.Code == StageForms.Project.Status);
        if (form?.TableName is null || status is null) return;
        var value = action == "pause" ? StageForms.Status.OnHold : StageForms.Status.Active;
        // Only the status column is written (UpdateAsync sets exactly the fields it is given).
        await repository.UpdateAsync(form.TableName, [status], projectId, new Dictionary<string, object?> { [status.Code] = value }, context.UserId, cancellationToken);
        data.Invalidate(StageForms.Projects);
        logger.LogInformation("Project {ProjectId} set to {Status} by the pause log", projectId, value);
    }
}

/// <summary>A project's plan rows and the floors are never deleted (owner, 2026-11-10:
/// deactivate instead) - old reports and the calculations keep pointing at them.</summary>
public sealed class ProjectPlanHook : IFormSubmissionHook
{
    public const string DeactivateInstead = "stage.deactivateInstead";
    public string FormCode => StageForms.ProjectPlan;

    public Task BeforeDeleteAsync(FormDefinition form, Guid recordId, CancellationToken cancellationToken) =>
        throw HookSupport.Refuse(DeactivateInstead, "is_active", "Stage plan rows are never deleted - set Active to No instead.");
}

public sealed class FloorsHook : IFormSubmissionHook
{
    public string FormCode => StageForms.Floors;

    public Task BeforeDeleteAsync(FormDefinition form, Guid recordId, CancellationToken cancellationToken) =>
        throw HookSupport.Refuse(ProjectPlanHook.DeactivateInstead, "is_active", "Floors are never deleted - set Active to No instead.");
}

/// <summary>
/// stage-plan-default and stage-plan-project rows (2026-11-10): one active row per stage + step
/// (per project), a unique order, a per-floor row with levels to cover, and one duration type
/// and level scope per stage - otherwise units and the schedule would be ambiguous.
/// </summary>
public abstract class PlanRowsHook(StageProgressData data) : IFormSubmissionHook
{
    public const string Duplicate = "stage.planDuplicate";
    public const string SortOrderTaken = "stage.planSortOrder";
    public const string NeedsLevels = "stage.planNeedsLevels";
    public const string Mixed = "stage.planMixed";

    public abstract string FormCode { get; }
    protected abstract bool PerProject { get; }

    public string? LockKey(SubmissionHookContext context) => $"stage-plan:{FormCode}";

    public async Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken)
    {
        var header = context.Submission.Header;
        var text = SubmissionPreparer.AsText(header);
        var row = new StagePlanRow(
            HookSupport.Int(header, StageForms.Plan.SortOrder) ?? 0, text.GetValueOrDefault(StageForms.Plan.StageCode) ?? "", "", "",
            string.IsNullOrWhiteSpace(text.GetValueOrDefault(StageForms.Plan.SubstepCode)) ? null : text[StageForms.Plan.SubstepCode], null, null,
            HookSupport.Int(header, StageForms.Plan.Days) ?? 0,
            StagePlanValues.ParseDurationType(text.GetValueOrDefault(StageForms.Plan.DurationType)),
            StagePlanValues.ParseLevelScope(text.GetValueOrDefault(StageForms.Plan.LevelScope)),
            StagePlanValues.ParseAppliesWhen(text.GetValueOrDefault(StageForms.Plan.AppliesWhen)),
            text.GetValueOrDefault(StageForms.Plan.IsActive) != "false");

        if (row.DurationType == DurationType.PerFloor && row.LevelScope == LevelScope.None)
            throw HookSupport.Refuse(NeedsLevels, StageForms.Plan.LevelScope, "A per-floor stage needs the levels it covers.");

        data.Invalidate();
        var (_, rows) = await data.FormAsync(FormCode, cancellationToken);
        var project = PerProject ? HookSupport.Guid(header, StageForms.Plan.Project) : null;
        var others = rows
            .Where(r => r.Id != context.RecordId && (!PerProject || StageProgressData.Id(r, StageForms.Plan.Project) == project))
            .Select(StageProgressData.ToPlanRow)
            .ToList();

        if (row.IsActive && others.Any(o => o.IsActive && o.StageCode == row.StageCode && o.SubstepCode == row.SubstepCode))
            throw HookSupport.Refuse(Duplicate, StageForms.Plan.SubstepCode, "This stage and step are already in the plan.");
        if (others.Any(o => o.SortOrder == row.SortOrder))
            throw HookSupport.Refuse(SortOrderTaken, StageForms.Plan.SortOrder, "Another row already has this order number.");
        if (others.Any(o => o.IsActive && o.StageCode == row.StageCode && (o.DurationType != row.DurationType || o.LevelScope != row.LevelScope)))
            throw HookSupport.Refuse(Mixed, StageForms.Plan.DurationType, "Every step of a stage needs the same duration type and levels.");
    }
}

public sealed class DefaultPlanRowsHook(StageProgressData data) : PlanRowsHook(data)
{
    public override string FormCode => StageForms.DefaultPlan;
    protected override bool PerProject => false;
}

public sealed class ProjectPlanRowsHook(StageProgressData data) : PlanRowsHook(data)
{
    public override string FormCode => StageForms.ProjectPlan;
    protected override bool PerProject => true;
}
