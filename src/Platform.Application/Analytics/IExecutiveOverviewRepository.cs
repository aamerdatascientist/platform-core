namespace Platform.Application.Analytics;

/// <summary>
/// Raw aggregate rows for the Executive Overview dashboard - hand-built Dapper SQL against
/// specific, known Operations/StockManagement Data_* tables, deliberately separate from
/// IDynamicDataRepository. IDynamicDataRepository is generic across ANY form (it takes the
/// active FieldDefinitions as a parameter and never hardcodes a field Code); every method
/// here hardcodes real field Codes ("headcount", "due_date", "status", ...) because it only
/// ever targets these specific forms - mixing the two shapes into one interface would make
/// IDynamicDataRepository's actual contract (works for any form) misleading.
/// </summary>
public interface IExecutiveOverviewRepository
{
    /// <summary>
    /// Labor Log's headcount, summed per (project, calendar day). Grouped by the Riyadh
    /// wall-clock date (see the implementation's comment) rather than the raw timestamptz
    /// value, so two rows for "the same day" always land in the same bucket even if their
    /// stored instants differ by time-of-day - nothing in the schema guarantees log_date is
    /// always submitted as a bare date.
    /// </summary>
    Task<IReadOnlyList<ProjectDateHeadcountRow>> GetCrewCountByProjectAndDateAsync(
        string laborLogTableName, CancellationToken cancellationToken = default);

    /// <summary>Daily Site Report row counts per (project, weather value) - the caller decides what counts as "impacted".</summary>
    Task<IReadOnlyList<ProjectWeatherCountRow>> GetWeatherCountsByProjectAsync(
        string dailySiteReportTableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every non-deleted Task Tracking row, unfiltered - "overdue" (due_date &lt; now AND
    /// status != completed) is evaluated in GetOverdueTasksQueryHandler, in plain C#, not
    /// here in SQL. That's deliberate: it's the one predicate in this dashboard explicitly
    /// called out as needing real unit-test coverage, and this sandbox can't run the
    /// Testcontainers-based integration tests that would be the only way to verify
    /// hand-built SQL text is actually correct (no Docker - see CLAUDE.md). Pushing the
    /// predicate into ordinary C# makes it exercisable by the same EF-InMemory-backed unit
    /// tests as everything else in this dashboard.
    /// </summary>
    Task<IReadOnlyList<TaskTrackingRow>> GetTaskRowsAsync(
        string taskTrackingTableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sum of <paramref name="quantityColumn"/> grouped by (material, <paramref name="locationColumn"/>)
    /// for one stock-movement table. Which column is "the location" varies by movement type
    /// (a caller concern, not this method's) - see GetStockMovementBreakdownQuery for the
    /// per-form mapping and why. The sum is returned exactly as stored, with no assumption
    /// about sign convention.
    /// </summary>
    Task<IReadOnlyList<MovementQuantityRow>> GetMovementQuantitiesAsync(
        string tableName, string quantityColumn, string locationColumn, CancellationToken cancellationToken = default);
}

public record ProjectDateHeadcountRow(Guid ProjectId, DateTime LogDate, int TotalHeadcount);

public record ProjectWeatherCountRow(Guid ProjectId, string Weather, int DayCount);

public record TaskTrackingRow(
    Guid TaskId, Guid ProjectId, string TaskReference, string Description,
    string AssignedTo, string Priority, string Status, DateTime? DueDateUtc);

public record MovementQuantityRow(Guid MaterialId, Guid LocationId, decimal TotalQuantity);
