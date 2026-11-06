using System.Globalization;
using System.Text.Json;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Analytics.Queries.GetExecutiveOverview;

/// <summary>
/// Everything the Executive Overview and the stakeholder reports calculate from, read once:
/// the projects, their registered unit counts, every daily progress report and every stock
/// movement, plus the display labels a report needs (a zone's name rather than its record Id,
/// "صافٍ" rather than "clear").
/// </summary>
public record OverviewInputs(
    IReadOnlyList<OverviewProjectRow> Projects,
    IReadOnlyDictionary<Guid, OverviewUnitCounts> UnitCounts,
    IReadOnlyList<OverviewReportRow> Reports,
    IReadOnlyList<OverviewStockRow> Stock,
    OverviewLabels Labels);

/// <summary>Display labels; every lookup falls back to the stored value, never throws.</summary>
public class OverviewLabels
{
    private readonly IReadOnlyDictionary<string, string> _units;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _options;

    public OverviewLabels(
        IReadOnlyDictionary<string, string> units,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> options)
    {
        _units = units;
        _options = options;
    }

    public static OverviewLabels Empty { get; } = new(
        new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>());

    /// <summary>The label of a dropdown value on the daily progress report form (weather, milestone, ...).</summary>
    public string Option(string fieldCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return _options.TryGetValue(fieldCode, out var labels) && labels.TryGetValue(value, out var label) ? label : value;
    }

    /// <summary>The unit a report is about, as people know it: "الطابق الثالث", "الكهرباء - الطابق 3".</summary>
    public string Unit(string phase, string? unitKey)
    {
        if (string.IsNullOrWhiteSpace(unitKey)) return string.Empty;
        return phase switch
        {
            OverviewProgressPlan.Paperwork => Option("milestone", unitKey),
            OverviewProgressPlan.Mep when unitKey.Split('@') is [var trade, var room] =>
                $"{Option("mep_trade", trade)} - {Option("mep_room", room)}",
            _ => _units.TryGetValue(unitKey, out var label) ? Prefix(phase) + label : unitKey,
        };
    }

    private static string Prefix(string phase) => phase switch
    {
        OverviewProgressPlan.Excavation => "المنطقة ",
        OverviewProgressPlan.Foundation => "القاعدة ",
        OverviewProgressPlan.Structural => "الطابق ",
        _ => string.Empty,
    };
}

/// <summary>Reads <see cref="OverviewInputs"/> from the live forms.</summary>
public class ExecutiveOverviewLoader
{
    /// <summary>
    /// Form DateTime fields are entered as Saudi-local calendar dates and stored as that
    /// local midnight in UTC (see DynamicDataRepository.ToDateTimeOffset) - a date typed as
    /// 10 Oct is stored as 9 Oct 21:00Z. Reading it back as a UTC date would put every report
    /// on the previous day, so every date here is shifted back to Riyadh time first.
    /// </summary>
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private const int PageSize = 200;
    private const string ReportFormCode = "daily-progress-report";

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public ExecutiveOverviewLoader(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<OverviewInputs> LoadAsync(bool withLabels, CancellationToken cancellationToken)
    {
        // A form that doesn't exist (or was never published) simply contributes no rows -
        // the dashboard degrades to empty panels rather than failing.
        var projectRows = (await ReadAllAsync("projects", cancellationToken)).Rows;
        var zoneRows = (await ReadAllAsync("project-zones", cancellationToken)).Rows;
        var footingRows = (await ReadAllAsync("project-footings", cancellationToken)).Rows;
        var floorRows = (await ReadAllAsync("project-floors", cancellationToken)).Rows;
        var (reportRows, reportForm) = await ReadAllAsync(ReportFormCode, cancellationToken);
        var inflowRows = (await ReadAllAsync("stock-inflow", cancellationToken)).Rows;
        var outflowRows = (await ReadAllAsync("stock-outflow", cancellationToken)).Rows;

        var projects = projectRows
            .Select(row => new OverviewProjectRow(
                row.Id,
                Text(row, "project_code") ?? string.Empty,
                Text(row, "project_name") ?? string.Empty,
                Text(row, "city"),
                Text(row, "project_manager"),
                Text(row, "status"),
                LocalDate(row, "start_date"),
                LocalDate(row, "expected_completion")))
            .ToList();

        var zones = CountByProject(zoneRows);
        var footings = CountByProject(footingRows);
        var floors = CountByProject(floorRows);
        var unitCounts = projects.ToDictionary(
            p => p.Id,
            p => new OverviewUnitCounts(
                zones.GetValueOrDefault(p.Id), footings.GetValueOrDefault(p.Id), floors.GetValueOrDefault(p.Id)));

        var reports = new List<OverviewReportRow>();
        foreach (var row in reportRows)
        {
            // A report with no project, date or phase can't be placed anywhere on the page.
            if (Id(row, "project") is not { } projectId || LocalDate(row, "report_date") is not { } date) continue;
            var phase = Text(row, "phase");
            if (phase is null) continue;

            reports.Add(new OverviewReportRow(
                projectId, date, phase, UnitKey(row, phase),
                Number(row, "crew_count"),
                Text(row, "weather"),
                Flag(row, "had_delay"),
                Text(row, "delay_cause"),
                Flag(row, "has_problem_today"),
                Text(row, "problem_description"),
                Flag(row, "plan_completed_today")));
        }

        var stock = new List<OverviewStockRow>();
        foreach (var row in inflowRows)
        {
            if (Id(row, "project") is not { } projectId || LocalDate(row, "date") is not { } date) continue;
            var material = Text(row, "material");
            if (material is null) continue;

            // "other" opens a free-text box on the form - the text typed there is the real material.
            var custom = material == "other" ? Text(row, "material_other") : null;
            stock.Add(new OverviewStockRow(
                projectId, date, IsInflow: true, custom ?? material, IsCustomMaterial: custom is not null,
                Amount(row, "quantity"), Text(row, "unit"), Reason: null));
        }
        foreach (var row in outflowRows)
        {
            if (Id(row, "project") is not { } projectId || LocalDate(row, "date") is not { } date) continue;
            var material = Text(row, "material");
            if (material is null) continue;

            stock.Add(new OverviewStockRow(
                projectId, date, IsInflow: false, material, IsCustomMaterial: false,
                Amount(row, "quantity"), Text(row, "unit"), Text(row, "reason")));
        }

        var labels = withLabels
            ? new OverviewLabels(
                UnitLabels(zoneRows, "zone_label", "zone_code")
                    .Concat(UnitLabels(footingRows, "footing_label", "footing_code"))
                    .Concat(UnitLabels(floorRows, "floor_label", "floor_code"))
                    .ToDictionary(x => x.Key, x => x.Value),
                OptionLabels(reportForm))
            : OverviewLabels.Empty;

        return new OverviewInputs(projects, unitCounts, reports, stock, labels);
    }

    /// <summary>
    /// Every non-deleted row of a form, fetched page by page (the repository caps a page at
    /// 200). Fine at today's volumes - a few hundred rows per form; if a form grows into the
    /// tens of thousands this should become SQL aggregation instead of reading everything.
    /// </summary>
    private async Task<(List<DynamicRow> Rows, FormDefinition? Form)> ReadAllAsync(string formCode, CancellationToken cancellationToken)
    {
        var form = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, formCode, cancellationToken);
        var rows = new List<DynamicRow>();
        if (form is null) return (rows, null);

        IReadOnlyCollection<FieldDefinition> activeFields =
            form.GetPublishedVersion()!.Fields.Where(f => f.IsActive).ToList();

        for (var page = 1; ; page++)
        {
            var result = await _dynamicDataRepository.QueryAsync(
                form.TableName!, activeFields, page, PageSize, cancellationToken: cancellationToken);
            rows.AddRange(result.Items);
            if (result.Items.Count == 0 || rows.Count >= result.TotalCount) break;
        }

        return (rows, form);
    }

    private static IEnumerable<KeyValuePair<string, string>> UnitLabels(IEnumerable<DynamicRow> rows, string labelField, string codeField) =>
        rows.Select(row => new KeyValuePair<string, string>(
            row.Id.ToString(), Text(row, labelField) ?? Text(row, codeField) ?? string.Empty));

    /// <summary>value -> label for every dropdown on the report form ([{"value","label"}] JSON).</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> OptionLabels(FormDefinition? form)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        if (form?.GetPublishedVersion() is not { } version) return result;

        foreach (var field in version.Fields.Where(f => !string.IsNullOrWhiteSpace(f.OptionsJson)))
        {
            try
            {
                using var json = JsonDocument.Parse(field.OptionsJson!);
                if (json.RootElement.ValueKind != JsonValueKind.Array) continue;
                var labels = new Dictionary<string, string>();
                foreach (var option in json.RootElement.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.Object &&
                        option.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String &&
                        option.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
                        labels[value.GetString()!] = label.GetString()!;
                }
                result[field.Code] = labels;
            }
            catch (JsonException)
            {
                // A malformed option list only costs labels, never the report.
            }
        }
        return result;
    }

    private static Dictionary<Guid, int> CountByProject(IEnumerable<DynamicRow> rows) =>
        rows.Select(row => Id(row, "project"))
            .Where(id => id is not null)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Which unit a report is about, within its phase. MEP has two selectors (trade and
    /// floor), so its key is both; every other phase has one.
    /// </summary>
    private static string? UnitKey(DynamicRow row, string phase) => phase switch
    {
        OverviewProgressPlan.Paperwork => Text(row, "milestone"),
        OverviewProgressPlan.Excavation => Id(row, "zone")?.ToString(),
        OverviewProgressPlan.Foundation => Id(row, "footing")?.ToString(),
        OverviewProgressPlan.Structural => Id(row, "floor")?.ToString(),
        OverviewProgressPlan.Mep =>
            Text(row, "mep_trade") is { } trade && Text(row, "mep_room") is { } room ? $"{trade}@{room}" : null,
        _ => null,
    };

    // DynamicRow values are loosely-typed objects straight from Dapper. Each reader below
    // returns "nothing" for a missing, null or unexpectedly-typed value instead of throwing -
    // one malformed row must not take the whole dashboard down.

    private static string? Text(DynamicRow row, string code)
    {
        var text = row.Values.GetValueOrDefault(code)?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static Guid? Id(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var parsed) => parsed,
        _ => null,
    };

    private static bool Flag(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) is true;

    private static int Number(DynamicRow row, string code)
    {
        try { return Convert.ToInt32(row.Values.GetValueOrDefault(code) ?? 0, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
    }

    private static decimal Amount(DynamicRow row, string code)
    {
        try { return Convert.ToDecimal(row.Values.GetValueOrDefault(code) ?? 0m, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0m; }
    }

    private static DateOnly? LocalDate(DynamicRow row, string code) => row.Values.GetValueOrDefault(code) switch
    {
        // Npgsql hands timestamptz back as a UTC DateTime; a Local kind only appears if the
        // driver's legacy timestamp mode is ever switched on, and is normalized the same way.
        DateTime dateTime => DateOnly.FromDateTime(
            (dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime) + SaudiArabiaOffset),
        DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.UtcDateTime + SaudiArabiaOffset),
        _ => null,
    };

    public static DateOnly SaudiToday(DateTime utcNow) => DateOnly.FromDateTime(utcNow + SaudiArabiaOffset);
}
