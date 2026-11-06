using Platform.Domain.Reports;

namespace Platform.Application.Reports;

// Everything one stakeholder report shows, as plain figures. The same object is what Claude
// reads (serialized as JSON) and what the PDF is drawn from, so the analysis can only talk
// about numbers the reader can also see. Percentages are whole numbers (67, not 0.67) and the
// differences a reader would want are precomputed, so the analysis never has to do arithmetic
// - which is also what lets ReportNumberCheck verify every number it writes.

public record StakeholderReport(
    string Kind,
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    ReportPortfolio Portfolio,
    IReadOnlyList<ReportProject> Projects,
    IReadOnlyList<ReportSeriesPoint> Series,
    IReadOnlyList<ReportSeriesPoint> Trend,
    IReadOnlyList<ReportCount> DelayCauses,
    IReadOnlyList<ReportCount> Weather,
    IReadOnlyList<ReportIssue> Issues)
{
    public bool IsDaily => Kind == ReportRun.Daily;
}

/// <summary>All projects together, for the period and the one before it.</summary>
public record ReportPortfolio(
    int ProjectCount,
    int Reports,
    int ExpectedReports,
    int MissingReports,
    int WorkedDays,
    int Crew,
    int CrewAveragePerDay,
    int? CrewAveragePerDayPrevious,
    int? CrewChange,
    int? PlanMetPercent,
    int? PlanMetPercentPrevious,
    int? PlanMetChangePoints,
    int Delays,
    int Problems,
    int LateDays,
    int? DelayCauseWindowDays);

public record ReportProject(
    string Code,
    string Name,
    string? City,
    string? Manager,
    string Tone,
    int WorkCompletePercent,
    int WorkCompletePercentAtStart,
    int WorkCompleteChangePoints,
    int? TimeElapsedPercent,
    IReadOnlyList<string> CurrentStages,
    IReadOnlyList<ReportStage> Stages,
    DateOnly? ForecastFinish,
    DateOnly? ContractEnd,
    int? DaysLate,
    int? DaysLateAtStart,
    int? DaysLateChange,
    int? PlanMetPercentLast30Days,
    int Reports,
    int? PlanMetPercent,
    int CrewAveragePerDay,
    int LateDays,
    int WorkedDays,
    int MissingReports,
    IReadOnlyList<ReportUnitDay> UnitReports,
    IReadOnlyList<string> MissingUnits,
    IReadOnlyList<ReportCount> TopDelayCauses);

/// <summary>
/// One stage of a project. LostDays = report days on which the plan wasn't met: the time the
/// stage took beyond the days it earned ("where the time went").
/// </summary>
public record ReportStage(
    string Stage,
    string Label,
    int PlannedDays,
    int EarnedDays,
    int ReportDays,
    int LostDays,
    int CompletePercent,
    int CompletePercentAtStart,
    bool Finished);

/// <summary>One report in a daily report's table.</summary>
public record ReportUnitDay(
    string Stage, string StageLabel, string Unit, int Crew, string Weather, bool PlanMet, string? DelayCause, string? Problem);

/// <summary>A day, week or month on a chart. PlanMetPercent is null when nothing was reported.</summary>
public record ReportSeriesPoint(DateOnly Date, string Label, int? PlanMetPercent, int Crew, int Reports);

public record ReportCount(string Label, int Count);

/// <summary>A delay, a problem or a missing report, for the "needs attention" list.</summary>
public record ReportIssue(string Type, DateOnly Date, string ProjectCode, string Unit, string Text);
