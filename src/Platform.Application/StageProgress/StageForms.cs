namespace Platform.Application.StageProgress;

/// <summary>
/// The forms and field codes the stage-progress module reads and writes (2026-11-10). These
/// forms are marked system forms, so their codes can't be renamed under the calculations.
/// </summary>
public static class StageForms
{
    public const string Projects = "projects";
    public const string Floors = "project-floors";
    public const string DefaultPlan = "stage-plan-default";
    public const string ProjectPlan = "stage-plan-project";
    public const string DailyReport = "daily-progress-report";
    public const string PauseLog = "project-pause-log";

    /// <summary>The daily report's repeatable section of stage blocks.</summary>
    public const string StagesSection = "stages";

    public static class Project
    {
        public const string Code = "project_code";
        public const string Name = "project_name";
        public const string Status = "status";
        public const string StartDate = "start_date";
        public const string ParkingLevels = "parking_levels";
        public const string ApartmentFloors = "apartment_floors";
        public const string NeedsDeconstruction = "needs_deconstruction";
        public const string NeedsSupport = "needs_support";
        public const string OutsideSides = "outside_sides";
    }

    public static class Floor
    {
        public const string Project = "project";
        public const string Code = "floor_code";
        public const string Label = "floor_label";
        public const string LabelEn = "label_en";
        public const string LevelNumber = "level_number";
        public const string LevelKind = "level_kind";
        public const string IsActive = "is_active";
    }

    public static class Plan
    {
        public const string Project = "project";
        public const string SortOrder = "sort_order";
        public const string StageCode = "stage_code";
        public const string StageNameEn = "stage_name_en";
        public const string StageNameAr = "stage_name_ar";
        public const string SubstepCode = "substep_code";
        public const string SubstepNameEn = "substep_name_en";
        public const string SubstepNameAr = "substep_name_ar";
        public const string Days = "days";
        public const string DurationType = "duration_type";
        public const string LevelScope = "level_scope";
        public const string AppliesWhen = "applies_when";
        public const string IsActive = "is_active";
    }

    public static class Report
    {
        public const string Project = "project";
        public const string Date = "report_date";
        public const string Stage = "stage";
        public const string Substep = "substep";
        public const string Level = "level";
        public const string Floors = "floors";
        public const string Sides = "sides";
        public const string PlanMet = "plan_met";
        public const string Finished = "finished";
    }

    public static class Pause
    {
        public const string Project = "project";
        public const string Action = "action";
        public const string Date = "date";
    }

    public static class Status
    {
        public const string Planning = "planning";
        public const string Active = "active";
        public const string OnHold = "on_hold";
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";
    }
}
