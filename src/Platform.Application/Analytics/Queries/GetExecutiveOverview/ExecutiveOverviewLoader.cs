using System.Globalization;
using System.Text.Json;
using Platform.Application.Common.Interfaces;
using Platform.Application.StageProgress;
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
    IReadOnlyDictionary<Guid, ProjectStageData> StageData,
    IReadOnlyList<OverviewReportRow> Reports,
    IReadOnlyList<OverviewStockRow> Stock,
    OverviewLabels Labels);

/// <summary>Display labels; every lookup falls back to the stored value, never throws.</summary>
public class OverviewLabels
{
    private readonly IReadOnlyDictionary<string, string> _units;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _options;
    private readonly IReadOnlyDictionary<string, string> _stages;

    public OverviewLabels(
        IReadOnlyDictionary<string, string> units,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> options,
        IReadOnlyDictionary<string, string>? stages = null)
    {
        _units = units;
        _options = options;
        _stages = stages ?? new Dictionary<string, string>();
    }

    public static OverviewLabels Empty { get; } = new(
        new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>());

    /// <summary>A stage's Arabic name from the plans (a v1 report's old phase: its option label).</summary>
    public string Stage(string code) =>
        _stages.TryGetValue(code, out var name) ? name : Option("phase", code);

    /// <summary>The label of a dropdown value on the daily progress report form (weather, milestone, ...).</summary>
    public string Option(string fieldCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return _options.TryGetValue(fieldCode, out var labels) && labels.TryGetValue(value, out var label) ? label : value;
    }

    /// <summary>A floor's name (v2 reports name floors by their project-floors row).</summary>
    public string Unit(string phase, string? unitKey) =>
        string.IsNullOrWhiteSpace(unitKey) ? string.Empty : _units.TryGetValue(unitKey, out var label) ? label : string.Empty;
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

    private readonly StageProgressData _stageData;

    public ExecutiveOverviewLoader(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, StageProgressData? stageData = null)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _stageData = stageData ?? new StageProgressData(db, dynamicDataRepository);
    }

    public async Task<OverviewInputs> LoadAsync(bool withLabels, CancellationToken cancellationToken)
    {
        // A form that doesn't exist (or was never published) simply contributes no rows -
        // the dashboard degrades to empty panels rather than failing.
        var projectRows = (await ReadAllAsync("projects", cancellationToken)).Rows;
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

        // Progress comes from each project's own stage plan and the reports' stage blocks.
        var stageData = new Dictionary<Guid, ProjectStageData>();
        foreach (var project in projects)
            if (await _stageData.ProjectDataAsync(project.Id, cancellationToken) is { Plan.Count: > 0 } data)
                stageData[project.Id] = data;

        // Report rows for the non-progress panels: a v1 report is one row under its old phase;
        // a v2 report gives one row per stage block (the block's own plan met and problem),
        // all carrying the report's id so crew and delays count once per report.
        var blocksByReport = await ReportBlocksAsync(reportForm, cancellationToken);
        var reports = new List<OverviewReportRow>();
        foreach (var row in reportRows)
        {
            // A report with no project or date can't be placed anywhere on the page.
            if (Id(row, "project") is not { } projectId || LocalDate(row, "report_date") is not { } date) continue;
            var crew = Number(row, "crew_count");
            var weather = Text(row, "weather");
            var delay = Flag(row, "had_delay");
            var cause = Text(row, "delay_cause");

            if (blocksByReport.TryGetValue(row.Id, out var blocks))
            {
                foreach (var block in blocks)
                {
                    if (BlockText(block, "stage") is not { } stage) continue;
                    reports.Add(new OverviewReportRow(
                        projectId, date, stage, null, crew, weather, delay, cause,
                        block.Values.GetValueOrDefault("has_problem") is true,
                        BlockText(block, "problem_details"),
                        block.Values.GetValueOrDefault("plan_met") is true,
                        row.Id));
                }
                continue;
            }

            var phase = Text(row, "phase");
            if (phase is null) continue;
            reports.Add(new OverviewReportRow(
                projectId, date, phase, null, crew, weather, delay, cause,
                Flag(row, "has_problem_today"),
                Text(row, "problem_description"),
                Flag(row, "plan_completed_today"),
                row.Id,
                FromStageBlock: false));
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
                UnitLabels(floorRows, "floor_label", "floor_code").ToDictionary(x => x.Key, x => x.Value),
                OptionLabels(reportForm),
                stageData.Values.SelectMany(d => d.Plan).GroupBy(r => r.StageCode).ToDictionary(g => g.Key, g => g.First().StageNameAr))
            : OverviewLabels.Empty;

        return new OverviewInputs(projects, stageData, reports, stock, labels);
    }

    private async Task<Dictionary<Guid, List<BlockRow>>> ReportBlocksAsync(FormDefinition? form, CancellationToken cancellationToken)
    {
        var version = form?.GetPublishedVersion();
        var section = version?.Sections.FirstOrDefault(s => s.Code == StageForms.StagesSection);
        if (form?.TableName is null || version is null || section?.ChildTableName is null) return new();
        var fields = version.Fields.Where(f => f.IsActive && f.SectionId == section.Id).ToList();
        var blocks = await _dynamicDataRepository.GetAllBlocksAsync(section.ChildTableName, form.TableName, fields, cancellationToken);
        return blocks.GroupBy(b => b.ParentId).ToDictionary(g => g.Key, g => g.OrderBy(b => b.BlockIndex).ToList());
    }

    private static string? BlockText(BlockRow row, string code) =>
        row.Values.GetValueOrDefault(code)?.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text) ? text : null;

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

        // Archived fields too: old reports keep their answers (2026-11-10 - the daily report's phase
        // questions were archived; old reports still feed the crew, weather, delay and problem panels).
        IReadOnlyCollection<FieldDefinition> activeFields =
            form.GetPublishedVersion()!.Fields.ToList();

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
