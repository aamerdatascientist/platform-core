using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.Application.StageProgress;

/// <summary>How a stage's planned days apply (stage-plan form, field <c>duration_type</c>).</summary>
public enum DurationType
{
    /// <summary>Done once for the whole project.</summary>
    Fixed,
    /// <summary>The days apply to each floor in the stage's level scope.</summary>
    PerFloor,
    /// <summary>The days apply to each outside side of the building.</summary>
    PerSide,
}

/// <summary>Which levels a per-floor stage covers (field <c>level_scope</c>).</summary>
public enum LevelScope { None, AllLevels, ApartmentFloorsOnly, ParkingOnly }

/// <summary>When a stage is part of a project at all (field <c>applies_when</c>).</summary>
public enum AppliesWhen { Always, IfDeconstruction, IfSupport }

public enum LevelKind { Parking, Apartment }

/// <summary>
/// One row of a stage plan - the default template (form <c>stage-plan-default</c>) or a
/// project's own copy (form <c>stage-plan-project</c>). A stage with sub-steps is several rows
/// sharing one <see cref="StageCode"/>; a stage without has one row and a null
/// <see cref="SubstepCode"/>. Codes are the stable keys reports store; names are display only.
/// </summary>
public sealed record StagePlanRow(
    int SortOrder,
    string StageCode,
    string StageNameEn,
    string StageNameAr,
    string? SubstepCode,
    string? SubstepNameEn,
    string? SubstepNameAr,
    int Days,
    DurationType DurationType,
    LevelScope LevelScope,
    AppliesWhen AppliesWhen,
    bool IsActive = true);

/// <summary>One level of a project (form <c>project-floors</c>): parking levels are 0, -1, ...;
/// apartment floors 1..N. Inactive levels are kept for old reports but have no units.</summary>
public sealed record ProjectLevel(Guid Id, int LevelNumber, LevelKind Kind, string LabelAr, string? LabelEn, bool IsActive = true);

/// <summary>What the stage calculations need to know about a project.</summary>
public sealed record ProjectShape(
    IReadOnlyList<ProjectLevel> Levels,
    int OutsideSides,
    bool NeedsDeconstruction,
    bool NeedsSupport)
{
    /// <summary>Active levels, bottom to top (parking first).</summary>
    public IReadOnlyList<ProjectLevel> ActiveLevels { get; } =
        Levels.Where(l => l.IsActive).OrderBy(l => l.LevelNumber).ToList();

    public bool Applies(StagePlanRow row) => row.AppliesWhen switch
    {
        AppliesWhen.IfDeconstruction => NeedsDeconstruction,
        AppliesWhen.IfSupport => NeedsSupport,
        _ => true,
    };

    /// <summary>The active levels a per-floor row covers, bottom to top.</summary>
    public IReadOnlyList<ProjectLevel> LevelsFor(LevelScope scope) => scope switch
    {
        LevelScope.AllLevels => ActiveLevels,
        LevelScope.ApartmentFloorsOnly => ActiveLevels.Where(l => l.Kind == LevelKind.Apartment).ToList(),
        LevelScope.ParkingOnly => ActiveLevels.Where(l => l.Kind == LevelKind.Parking).ToList(),
        _ => Array.Empty<ProjectLevel>(),
    };
}

/// <summary>
/// The rules that name particular stages. Structure and walls are the only two stages with
/// their own schedule (walls on level i start when structure starts on level i+2) and
/// structure is the only one reported one level at a time (owner, 2026-11-10: identify them
/// by stage code).
/// </summary>
public static class StageCodes
{
    public const string Structure = "structure";
    public const string Walls = "walls";
}

/// <summary>The default stage plan, read from the embedded <c>default-stage-plan.json</c> -
/// the same file the staging seed script sends to the <c>stage-plan-default</c> form.</summary>
public static class DefaultStagePlan
{
    private static readonly Lazy<IReadOnlyList<StagePlanRow>> Cached = new(Load);

    public static IReadOnlyList<StagePlanRow> Rows => Cached.Value;

    private static IReadOnlyList<StagePlanRow> Load()
    {
        var assembly = typeof(DefaultStagePlan).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("default-stage-plan.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return Parse(stream);
    }

    public static IReadOnlyList<StagePlanRow> Parse(Stream json)
    {
        var rows = JsonSerializer.Deserialize<List<JsonRow>>(json, Options) ?? [];
        return rows.Select(r => new StagePlanRow(
                r.SortOrder, r.StageCode, r.StageNameEn, r.StageNameAr, r.SubstepCode, r.SubstepNameEn, r.SubstepNameAr,
                r.Days, StagePlanValues.ParseDurationType(r.DurationType), StagePlanValues.ParseLevelScope(r.LevelScope),
                StagePlanValues.ParseAppliesWhen(r.AppliesWhen)))
            .OrderBy(r => r.SortOrder)
            .ToList();
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record JsonRow(
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("stage_code")] string StageCode,
        [property: JsonPropertyName("stage_name_en")] string StageNameEn,
        [property: JsonPropertyName("stage_name_ar")] string StageNameAr,
        [property: JsonPropertyName("substep_code")] string? SubstepCode,
        [property: JsonPropertyName("substep_name_en")] string? SubstepNameEn,
        [property: JsonPropertyName("substep_name_ar")] string? SubstepNameAr,
        [property: JsonPropertyName("days")] int Days,
        [property: JsonPropertyName("duration_type")] string DurationType,
        [property: JsonPropertyName("level_scope")] string LevelScope,
        [property: JsonPropertyName("applies_when")] string AppliesWhen);
}

/// <summary>The stored option values of the stage-plan forms' dropdowns, both ways.</summary>
public static class StagePlanValues
{
    public static DurationType ParseDurationType(string? value) => value switch
    {
        "per_floor" => DurationType.PerFloor,
        "per_side" => DurationType.PerSide,
        _ => DurationType.Fixed,
    };

    public static LevelScope ParseLevelScope(string? value) => value switch
    {
        "all_levels" => LevelScope.AllLevels,
        "apartment_floors_only" => LevelScope.ApartmentFloorsOnly,
        "parking_only" => LevelScope.ParkingOnly,
        _ => LevelScope.None,
    };

    public static AppliesWhen ParseAppliesWhen(string? value) => value switch
    {
        "if_deconstruction" => AppliesWhen.IfDeconstruction,
        "if_support" => AppliesWhen.IfSupport,
        _ => AppliesWhen.Always,
    };

    public static string ToValue(DurationType v) => v switch
    {
        DurationType.PerFloor => "per_floor",
        DurationType.PerSide => "per_side",
        _ => "fixed",
    };

    public static string ToValue(LevelScope v) => v switch
    {
        LevelScope.AllLevels => "all_levels",
        LevelScope.ApartmentFloorsOnly => "apartment_floors_only",
        LevelScope.ParkingOnly => "parking_only",
        _ => "none",
    };

    public static string ToValue(AppliesWhen v) => v switch
    {
        AppliesWhen.IfDeconstruction => "if_deconstruction",
        AppliesWhen.IfSupport => "if_support",
        _ => "always",
    };
}
