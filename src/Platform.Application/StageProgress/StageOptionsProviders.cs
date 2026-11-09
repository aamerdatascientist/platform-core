using Platform.Application.Forms;

namespace Platform.Application.StageProgress;

/// <summary>
/// Attribute values the daily report's stage block shows its fields by (set on the fields'
/// VisibleWhenAttribute): <c>has_substeps</c> true/false; <c>unit_pick</c> one of
/// <c>one_level</c> (structure: one level a day), <c>floors</c> (other per-floor stages),
/// <c>sides</c> (per-side stages), <c>none</c> (fixed stages).
/// </summary>
public static class StageAttributes
{
    public const string HasSubsteps = "has_substeps";
    public const string UnitPick = "unit_pick";
    public const string OneLevel = "one_level";
    public const string Floors = "floors";
    public const string Sides = "sides";
    public const string None = "none";
}

internal static class StageProviderSupport
{
    public static Guid? ProjectOf(FieldOptionsContext context) =>
        Guid.TryParse(context.Values.GetValueOrDefault(StageForms.Report.Project), out var id) ? id : null;

    /// <summary>The project's active plan rows that apply to it, in order.</summary>
    public static async Task<(StageProject Project, IReadOnlyList<StagePlanRow> Rows)?> PlanAsync(
        StageProgressData data, FieldOptionsContext context, CancellationToken ct)
    {
        if (ProjectOf(context) is not { } projectId || await data.ProjectAsync(projectId, ct) is not { } project) return null;
        var rows = StageScheduleBuilder.Applicable(await data.ProjectPlanAsync(projectId, ct), project.Shape);
        return (project, rows);
    }
}

/// <summary>The stages of the chosen project's own plan: active, applying to the project, in order.</summary>
public sealed class StageOptionsProvider(StageProgressData data) : IFieldOptionsProvider
{
    public string Key => "stage-plan.stages";
    public string Description => "Stages of the chosen project's stage plan (needs a 'project' field)";

    public async Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken)
    {
        if (await StageProviderSupport.PlanAsync(data, context, cancellationToken) is not { } plan) return [];
        return plan.Rows.GroupBy(r => r.StageCode).Select(g =>
        {
            var first = g.First();
            var unitPick = first.StageCode == StageCodes.Structure ? StageAttributes.OneLevel
                : first.DurationType switch
                {
                    DurationType.PerFloor => StageAttributes.Floors,
                    DurationType.PerSide => StageAttributes.Sides,
                    _ => StageAttributes.None,
                };
            return new FieldOption(first.StageCode, first.StageNameAr, first.StageNameEn, new Dictionary<string, string>
            {
                [StageAttributes.HasSubsteps] = g.Any(r => r.SubstepCode is not null) ? "true" : "false",
                [StageAttributes.UnitPick] = unitPick,
            });
        }).ToList();
    }
}

/// <summary>The sub-steps of the chosen stage, in order.</summary>
public sealed class SubstepOptionsProvider(StageProgressData data) : IFieldOptionsProvider
{
    public string Key => "stage-plan.substeps";
    public string Description => "Sub-steps of the chosen stage (needs 'project' and 'stage' fields)";

    public async Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken)
    {
        if (await StageProviderSupport.PlanAsync(data, context, cancellationToken) is not { } plan) return [];
        var stage = context.Values.GetValueOrDefault(StageForms.Report.Stage);
        return plan.Rows.Where(r => r.StageCode == stage && r.SubstepCode is not null)
            .Select(r => new FieldOption(r.SubstepCode!, r.SubstepNameAr ?? r.SubstepCode!, r.SubstepNameEn))
            .ToList();
    }
}

/// <summary>The project's active floors within the chosen stage's level scope, bottom to top.</summary>
public sealed class FloorOptionsProvider(StageProgressData data) : IFieldOptionsProvider
{
    public string Key => "stage-plan.floors";
    public string Description => "Floors of the chosen project in the chosen stage's level scope";

    public async Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken)
    {
        if (await StageProviderSupport.PlanAsync(data, context, cancellationToken) is not { } plan) return [];
        var stage = context.Values.GetValueOrDefault(StageForms.Report.Stage);
        var row = plan.Rows.FirstOrDefault(r => r.StageCode == stage && r.DurationType == DurationType.PerFloor);
        var levels = row is null ? plan.Project.Shape.ActiveLevels : plan.Project.Shape.LevelsFor(row.LevelScope);
        return levels.Select(l => new FieldOption(l.Id.ToString(), l.LabelAr, l.LabelEn)).ToList();
    }
}

/// <summary>Side 1 .. N of the chosen project (its outside_sides).</summary>
public sealed class SideOptionsProvider(StageProgressData data) : IFieldOptionsProvider
{
    public string Key => "stage-plan.sides";
    public string Description => "Outside sides 1..N of the chosen project";

    public async Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken)
    {
        if (StageProviderSupport.ProjectOf(context) is not { } projectId || await data.ProjectAsync(projectId, cancellationToken) is not { } project)
            return [];
        return Enumerable.Range(1, Math.Max(0, project.Shape.OutsideSides))
            .Select(n => new FieldOption(n.ToString(), $"الواجهة {n}", $"Side {n}"))
            .ToList();
    }
}
