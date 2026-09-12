using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Platform.Application.Analytics;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

/// <summary>
/// Hand-built Dapper SQL against specific, known Operations/StockManagement tables - see
/// IExecutiveOverviewRepository's own doc comment for why this is deliberately separate
/// from the generic IDynamicDataRepository/DynamicDataRepository. Table and column names
/// still go through AssertSafePostgresIdentifier before interpolation, even though every
/// caller today passes compile-time literals - "trust the caller" is not a security
/// boundary on its own, same rule DynamicDataRepository already follows.
/// </summary>
public class ExecutiveOverviewRepository : IExecutiveOverviewRepository
{
    private readonly string _connectionString;

    public ExecutiveOverviewRepository(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("PostgresConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgresConnection is not configured.");

    public async Task<IReadOnlyList<ProjectDateHeadcountRow>> GetCrewCountByProjectAndDateAsync(
        string laborLogTableName, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(laborLogTableName);

        // Grouped by the Riyadh wall-clock calendar date, not the raw timestamptz value -
        // log_date represents a business day, but nothing stops it being submitted with a
        // non-midnight time-of-day, which would otherwise split "the same day" across
        // multiple buckets. AT TIME ZONE converts the stored UTC instant to Riyadh local
        // time first, matching the documented UTC+3 default for dynamic form DateTime
        // fields (see CLAUDE.md), then truncates to a date.
        var sql = $"""
            SELECT "project" AS "ProjectId",
                   (("log_date" AT TIME ZONE 'Asia/Riyadh')::date) AS "LogDate",
                   SUM("headcount")::int AS "TotalHeadcount"
            FROM "{laborLogTableName}"
            WHERE "IsDeleted" = false
            GROUP BY "project", (("log_date" AT TIME ZONE 'Asia/Riyadh')::date);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<ProjectDateHeadcountRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ProjectWeatherCountRow>> GetWeatherCountsByProjectAsync(
        string dailySiteReportTableName, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(dailySiteReportTableName);

        var sql = $"""
            SELECT "project" AS "ProjectId", "weather" AS "Weather", COUNT(1)::int AS "DayCount"
            FROM "{dailySiteReportTableName}"
            WHERE "IsDeleted" = false
            GROUP BY "project", "weather";
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<ProjectWeatherCountRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TaskTrackingRow>> GetTaskRowsAsync(
        string taskTrackingTableName, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(taskTrackingTableName);

        // Unfiltered on purpose - see IExecutiveOverviewRepository's doc comment on this
        // method for why "overdue" is decided in the query handler, not here.
        var sql = $"""
            SELECT "Id" AS "TaskId", "project" AS "ProjectId", "task_reference" AS "TaskReference",
                   "task_description" AS "Description", "assigned_to" AS "AssignedTo", "priority" AS "Priority",
                   "status" AS "Status", "due_date" AS "DueDateUtc"
            FROM "{taskTrackingTableName}"
            WHERE "IsDeleted" = false;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<TaskTrackingRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MovementQuantityRow>> GetMovementQuantitiesAsync(
        string tableName, string quantityColumn, string locationColumn, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        SqlTypeMapper.AssertSafePostgresIdentifier(quantityColumn);
        SqlTypeMapper.AssertSafePostgresIdentifier(locationColumn);

        var sql = $"""
            SELECT "material" AS "MaterialId", "{locationColumn}" AS "LocationId", SUM("{quantityColumn}") AS "TotalQuantity"
            FROM "{tableName}"
            WHERE "IsDeleted" = false
            GROUP BY "material", "{locationColumn}";
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<MovementQuantityRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
