using Platform.Application.Analytics;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// Stub-and-spy test double for IExecutiveOverviewRepository. GetMovementQuantitiesAsync
/// records every call's (tableName, quantityColumn, locationColumn) - the per-table column
/// mapping in GetStockMovementBreakdownQueryHandler (e.g. Material Issue uses from_location,
/// not to_location) is exactly the kind of thing that's easy to get backwards, so tests
/// assert on what was actually requested, not just on the DTOs that came back.
/// </summary>
public class FakeExecutiveOverviewRepository : IExecutiveOverviewRepository
{
    public Dictionary<string, List<ProjectDateHeadcountRow>> CrewCounts { get; } = new();
    public Dictionary<string, List<ProjectWeatherCountRow>> WeatherCounts { get; } = new();
    public Dictionary<string, List<TaskTrackingRow>> TaskRows { get; } = new();
    public Dictionary<string, List<MovementQuantityRow>> MovementQuantities { get; } = new();
    public List<(string TableName, string QuantityColumn, string LocationColumn)> MovementQuantityCalls { get; } = new();

    public Task<IReadOnlyList<ProjectDateHeadcountRow>> GetCrewCountByProjectAndDateAsync(
        string laborLogTableName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectDateHeadcountRow>>(CrewCounts.GetValueOrDefault(laborLogTableName, new()));

    public Task<IReadOnlyList<ProjectWeatherCountRow>> GetWeatherCountsByProjectAsync(
        string dailySiteReportTableName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectWeatherCountRow>>(WeatherCounts.GetValueOrDefault(dailySiteReportTableName, new()));

    public Task<IReadOnlyList<TaskTrackingRow>> GetTaskRowsAsync(
        string taskTrackingTableName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskTrackingRow>>(TaskRows.GetValueOrDefault(taskTrackingTableName, new()));

    public Task<IReadOnlyList<MovementQuantityRow>> GetMovementQuantitiesAsync(
        string tableName, string quantityColumn, string locationColumn, CancellationToken cancellationToken = default)
    {
        MovementQuantityCalls.Add((tableName, quantityColumn, locationColumn));
        return Task.FromResult<IReadOnlyList<MovementQuantityRow>>(MovementQuantities.GetValueOrDefault(tableName, new()));
    }
}
