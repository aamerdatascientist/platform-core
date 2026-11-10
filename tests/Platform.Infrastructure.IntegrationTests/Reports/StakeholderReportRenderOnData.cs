using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.Reports;
using Platform.Domain.Reports;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.DynamicSchema;
using Platform.Infrastructure.Reports;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Manual check, not part of the suite: with REPORT_DB set to a database connection string and
/// REPORT_OUT to a folder, renders the weekly stakeholder report from that database's real data
/// (no Claude call, no PDF service) so a person can look at it. Does nothing otherwise.
/// </summary>
public class StakeholderReportRenderOnData
{
    [Fact]
    public async Task RenderWeeklyReportFromADatabase()
    {
        var connection = Environment.GetEnvironmentVariable("REPORT_DB");
        var output = Environment.GetEnvironmentVariable("REPORT_OUT");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(output)) return;
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PostgresConnection"] = connection }).Build();
        var inputs = await new ExecutiveOverviewLoader(db, new DynamicDataRepository(configuration)).LoadAsync(withLabels: true, CancellationToken.None);
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        foreach (var kind in new[] { ReportRun.Daily, ReportRun.Weekly })
        {
            var report = StakeholderReportBuilder.Build(inputs, kind, to);
            await File.WriteAllTextAsync(Path.Combine(output, $"report-{kind}.html"), new ReportHtmlRenderer().RenderHtml(report, null, null, phone: false));
            await File.WriteAllTextAsync(Path.Combine(output, $"report-{kind}.json"), ReportFigures.ToJson(report));
        }
    }
}
