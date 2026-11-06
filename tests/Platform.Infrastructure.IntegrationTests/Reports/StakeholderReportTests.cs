using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.Common.Interfaces;
using Platform.Application.Reports;
using Platform.Domain.Reports;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Reports;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Stakeholder reports: the periods and schedule, the figures, the number check that keeps the
/// analysis honest, the $15 monthly cap, and delivery - with fakes for Claude, the PDF service,
/// storage and WhatsApp (no network).
/// </summary>
public class StakeholderReportTests
{
    // ------------------------------------------------------------------ periods and schedule

    [Fact]
    public void Period_Weekly_RunsSaturdayToTheDay_AndComparesWithTheWeekBefore()
    {
        var thursday = new DateOnly(2026, 11, 5);
        var (from, to, prevFrom, prevTo) = StakeholderReportBuilder.Period(ReportRun.Weekly, thursday);
        from.Should().Be(new DateOnly(2026, 10, 31));
        from.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        to.Should().Be(thursday);
        prevFrom.Should().Be(new DateOnly(2026, 10, 24));
        prevTo.Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public void Period_Monthly_IsTheCalendarMonth_AndComparesWithThePreviousMonth()
    {
        var (from, to, prevFrom, prevTo) = StakeholderReportBuilder.Period(ReportRun.Monthly, new DateOnly(2026, 11, 30));
        from.Should().Be(new DateOnly(2026, 11, 1));
        to.Should().Be(new DateOnly(2026, 11, 30));
        prevFrom.Should().Be(new DateOnly(2026, 10, 1));
        prevTo.Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void Period_Daily_ComparesWithTheTwoWeeksBefore()
    {
        var day = new DateOnly(2026, 11, 1);
        StakeholderReportBuilder.Period(ReportRun.Daily, day)
            .Should().Be((day, day, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 31)));
    }

    [Theory]
    [InlineData("2026-11-26", new[] { "daily", "weekly" })]          // Thursday
    [InlineData("2026-11-27", new string[0])]                        // Friday: nothing
    [InlineData("2026-11-30", new[] { "daily", "monthly" })]         // Monday, month end
    [InlineData("2027-04-30", new[] { "monthly" })]                  // month end on a Friday: still the monthly
    [InlineData("2026-12-31", new[] { "daily", "weekly", "monthly" })] // Thursday and month end
    public void DueKinds_FollowsTheOwnersSchedule(string day, string[] expected) =>
        StakeholderReportScheduler.DueKinds(DateOnly.Parse(day)).Should().Equal(expected);

    [Theory]
    [InlineData("17:59", false)]
    [InlineData("18:00", true)]
    [InlineData("20:59", true)]
    [InlineData("21:00", false)]
    public void IsSendWindow_OpensAtSixForThreeHours(string now, bool expected) =>
        StakeholderReportScheduler.IsSendWindow("18:00", TimeOnly.Parse(now)).Should().Be(expected);

    // ------------------------------------------------------------------ figures

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Floor2 = Guid.NewGuid();
    private static readonly Guid Floor3 = Guid.NewGuid();

    private static OverviewInputs Inputs(params OverviewReportRow[] reports) => new(
        new[] { new OverviewProjectRow(ProjectId, "119", "نزل الأخيار", "مكة", "محمد قاسم", "active", new DateOnly(2026, 1, 20), new DateOnly(2026, 9, 15)) },
        new Dictionary<Guid, OverviewUnitCounts> { [ProjectId] = new(1, 2, 6) },
        reports,
        Array.Empty<OverviewStockRow>(),
        new OverviewLabels(
            new Dictionary<string, string> { [Floor2.ToString()] = "الثاني", [Floor3.ToString()] = "الثالث" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["weather"] = new Dictionary<string, string> { ["clear"] = "صافٍ" },
            }));

    private static OverviewReportRow Report(DateOnly date, Guid floor, int crew, bool met, string? cause = null) =>
        new(ProjectId, date, OverviewProgressPlan.Structural, floor.ToString(), crew, "clear", cause is not null, cause, false, null, met);

    [Fact]
    public void Build_Daily_CountsTodaysReports_AndFindsTheUnitThatDidNotReport()
    {
        var saturday = new DateOnly(2026, 10, 31);
        var sunday = new DateOnly(2026, 11, 1);
        var inputs = Inputs(
            Report(saturday, Floor2, 6, met: true),
            Report(saturday, Floor3, 5, met: true),
            Report(sunday, Floor3, 4, met: false, cause: "تأخر توريد الخرسانة"));

        var report = StakeholderReportBuilder.Build(inputs, ReportRun.Daily, sunday);

        report.Portfolio.Reports.Should().Be(1);
        report.Portfolio.ExpectedReports.Should().Be(2);
        report.Portfolio.Crew.Should().Be(4);
        report.Portfolio.PlanMetPercent.Should().Be(0);
        report.Portfolio.PlanMetPercentPrevious.Should().Be(100);
        report.Portfolio.PlanMetChangePoints.Should().Be(-100);
        var project = report.Projects.Single();
        project.MissingUnits.Should().Equal("الطابق الثاني");
        project.UnitReports.Single().Should().Be(new ReportUnitDay(
            "structural", "الهيكل الإنشائي", "الطابق الثالث", 4, "صافٍ", false, "تأخر توريد الخرسانة", null));
        report.Issues.Select(i => i.Type).Should().BeEquivalentTo(new[] { "delay", "missing" });
        report.Series.Should().HaveCount(StakeholderReportBuilder.DailySeriesDays);
        report.Series.Should().NotContain(p => p.Date.DayOfWeek == DayOfWeek.Friday);
    }

    [Fact]
    public void Build_Stages_CountTheDaysTheyLost()
    {
        var day = new DateOnly(2026, 11, 1);
        var inputs = Inputs(
            Report(day.AddDays(-3), Floor2, 6, met: false, cause: "حرارة"),
            Report(day.AddDays(-2), Floor2, 6, met: true),
            Report(day.AddDays(-1), Floor2, 6, met: true),
            Report(day, Floor2, 6, met: true));

        var stage = StakeholderReportBuilder.Build(inputs, ReportRun.Daily, day)
            .Projects.Single().Stages.Single(s => s.Stage == OverviewProgressPlan.Structural);

        stage.PlannedDays.Should().Be(OverviewProgressPlan.FloorDays * 6);
        stage.ReportDays.Should().Be(4);
        stage.EarnedDays.Should().Be(3);
        stage.LostDays.Should().Be(1);
        stage.Finished.Should().BeFalse();
    }

    [Fact]
    public void Build_Weekly_ComparesProgressWithTheStartOfTheWeek()
    {
        var thursday = new DateOnly(2026, 11, 5);
        var lastWeek = Enumerable.Range(0, 6).Select(i => Report(new DateOnly(2026, 10, 24).AddDays(i), Floor2, 5, met: true));
        var thisWeek = new[] { 0, 1, 2, 3, 4, 5 }.Select(i => new DateOnly(2026, 10, 31).AddDays(i))
            .Where(StakeholderReportBuilder.IsWorkingDay)
            .Select(d => Report(d, Floor2, 7, met: d.DayOfWeek != DayOfWeek.Monday));
        var inputs = Inputs(lastWeek.Concat(thisWeek).ToArray());

        var report = StakeholderReportBuilder.Build(inputs, ReportRun.Weekly, thursday);

        report.Portfolio.Reports.Should().Be(6);
        report.Portfolio.CrewAveragePerDay.Should().Be(7);
        report.Portfolio.CrewChange.Should().Be(2);
        report.Portfolio.PlanMetPercent.Should().Be(83);
        var project = report.Projects.Single();
        project.WorkCompleteChangePoints.Should().BeGreaterThan(0);
        project.MissingReports.Should().Be(0);
        report.Trend.Should().HaveCount(StakeholderReportBuilder.TrendWeeks);
    }

    // ------------------------------------------------------------------ the number check

    private static ReportAnalysis Analysis(string summary) => new(
        summary,
        new[] { new ReportFinding("+111", "bad", "تأخير", "نص") },
        new ReportChartNotes("", "", ""),
        Array.Empty<ReportProjectAnalysis>(),
        Array.Empty<ReportRecommendation>(),
        Array.Empty<string>());

    [Fact]
    public void NumberCheck_AcceptsNumbersFromTheFigures_InEitherDigitSet()
    {
        const string figures = """{"report":{"daysLate":111,"planMetPercent":60,"to":"2026-11-01"}}""";
        ReportNumberCheck.UnknownNumbers(Analysis("أُنجزت الخطة في ٦٠% وتأخر 111 يوماً حتى 1 نوفمبر 2026، أي 3 مشاريع."), figures)
            .Should().BeEmpty();
    }

    [Fact]
    public void NumberCheck_RejectsANumberClaudeCalculatedItself()
    {
        const string figures = """{"report":{"daysLate":111,"planMetPercent":60}}""";
        ReportNumberCheck.UnknownNumbers(Analysis("الفرق 51 نقطة"), figures).Should().Equal("51");
    }

    // ------------------------------------------------------------------ the service: budget, retries, delivery

    private sealed class FakeAnalyst : IReportAnalyst
    {
        public Queue<AnalystResult> Answers { get; } = new();
        public List<string?> Corrections { get; } = new();

        public Task<AnalystResult> AnalyseAsync(string kind, string figuresJson, string? correction, CancellationToken cancellationToken)
        {
            Corrections.Add(correction);
            return Task.FromResult(Answers.Dequeue());
        }
    }

    private sealed class FakeRenderer : IReportRenderer
    {
        public List<(ReportAnalysis? Analysis, string? Note, bool Phone)> Calls { get; } = new();

        public string RenderHtml(StakeholderReport report, ReportAnalysis? analysis, string? analysisNote, bool phone)
        {
            Calls.Add((analysis, analysisNote, phone));
            return "<html></html>";
        }
    }

    private sealed class FakePdf : IPdfRenderer
    {
        public Task<byte[]> RenderAsync(string html, bool phone, CancellationToken cancellationToken) =>
            Task.FromResult(phone ? new byte[] { 2 } : new byte[] { 1 });
    }

    private sealed class FakeBlobs : IBlobStorageService
    {
        public List<string> Uploaded { get; } = new();
        public Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            Uploaded.Add(blobName);
            return Task.CompletedTask;
        }
        public Task<string> GetReadUrlAsync(string blobName, TimeSpan validFor, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://example/" + blobName);
        public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeWhatsApp : IWhatsAppDocumentSender
    {
        public List<(string To, byte[] Pdf, IReadOnlyList<string> Parameters)> Sent { get; } = new();
        public Task<NotificationSendResult> SendDocumentAsync(
            string to, string templateName, byte[] pdf, string fileName, IReadOnlyList<string> bodyParameters,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((to, pdf, bodyParameters));
            return Task.FromResult(new NotificationSendResult(true, "wamid.1", null));
        }
    }

    private sealed class Harness
    {
        public ApplicationDbContext Db { get; } =
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeAnalyst Analyst { get; } = new();
        public FakeRenderer Renderer { get; } = new();
        public FakeBlobs Blobs { get; } = new();
        public FakeWhatsApp WhatsApp { get; } = new();
        public ReportSettings Settings { get; } = new() { AnthropicApiKey = "test", PdfServiceUrl = "http://pdf" };

        public StakeholderReportService Service() => new(
            Db, new FakeDynamicDataRepository(), Analyst, Renderer, new FakePdf(), Blobs, WhatsApp, Settings,
            new WhatsAppSettings { PhoneNumberId = "1", AccessToken = "t" }, NullLogger<StakeholderReportService>.Instance);

        public async Task SpendAsync(decimal usd)
        {
            var run = ReportRun.Start(ReportRun.Daily, Today, Today, ReportRun.Preview, null, DateTime.UtcNow);
            run.RecordAnalysis(ReportRun.AnalysisWritten, 0, 0, usd);
            Db.ReportRuns.Add(run);
            await Db.SaveChangesAsync();
        }
    }

    private static readonly DateOnly Today = new(2026, 11, 1);

    private static AnalystResult Written(string summary = "يوم عادي") => new(Analysis(summary) with { Findings = Array.Empty<ReportFinding>() }, 12_000, 6_000, null);

    [Fact]
    public async Task Run_WritesTheAnalysis_RecordsItsCost_AndSendsThePhonePdf()
    {
        var h = new Harness();
        h.Db.ReportRecipients.Add(ReportRecipient.Create("المالك", "966500000001", daily: true, weekly: true, monthly: true));
        var off = ReportRecipient.Create("آخر", "966500000002", daily: false, weekly: true, monthly: true);
        h.Db.ReportRecipients.Add(off);
        await h.Db.SaveChangesAsync();
        h.Analyst.Answers.Enqueue(Written());

        var result = await h.Service().RunAsync(ReportRun.Daily, Today, ReportRun.Schedule, null, withAnalysis: true, deliver: true, CancellationToken.None);

        var run = result!.Run;
        run.Status.Should().Be(ReportRun.Done);
        run.Analysis.Should().Be(ReportRun.AnalysisWritten);
        run.CostUsd.Should().Be(12_000 * 4m / 1_000_000 + 6_000 * 20m / 1_000_000);
        h.Renderer.Calls.Should().OnlyContain(c => c.Analysis != null);
        h.Blobs.Uploaded.Should().HaveCount(2);
        h.WhatsApp.Sent.Should().ContainSingle();
        h.WhatsApp.Sent[0].To.Should().Be("966500000001", "only the recipient who receives daily reports");
        h.WhatsApp.Sent[0].Pdf.Should().Equal(new byte[] { 2 }, "the phone-sized PDF is the one sent");
        h.WhatsApp.Sent[0].Parameters.Should().Equal("المالك", "التقرير اليومي", "الأحد 1 نوفمبر 2026");
        (await h.Db.ReportDeliveries.SingleAsync()).Status.Should().Be("sent");
    }

    [Fact]
    public async Task Run_SkipsClaude_WhenTheCallCouldPassTheMonthlyCap()
    {
        var h = new Harness();
        await h.SpendAsync(14.80m);

        var result = await h.Service().RunAsync(ReportRun.Daily, Today, ReportRun.Preview, null, withAnalysis: true, deliver: false, CancellationToken.None);

        h.Analyst.Corrections.Should().BeEmpty("Claude must not be called at all");
        result!.Run.Analysis.Should().Be(ReportRun.AnalysisSkippedBudget);
        result.Run.Status.Should().Be(ReportRun.Done, "the report still goes out, without the written analysis");
        h.Renderer.Calls.Should().OnlyContain(c => c.Analysis == null && c.Note != null);
    }

    [Fact]
    public async Task Run_CallsClaude_WhileTheWorstCaseStillFitsTheCap()
    {
        var h = new Harness();
        await h.SpendAsync(10m);
        h.Analyst.Answers.Enqueue(Written());

        var result = await h.Service().RunAsync(ReportRun.Daily, Today, ReportRun.Preview, null, true, false, CancellationToken.None);

        result!.Run.Analysis.Should().Be(ReportRun.AnalysisWritten);
        (await h.Service().SpentThisMonthAsync(DateTime.UtcNow, CancellationToken.None)).Should().BeGreaterThan(10m);
    }

    [Fact]
    public async Task Run_RetriesOnce_WhenTheAnalysisInventsANumber_ThenSendsWithoutIt()
    {
        var h = new Harness();
        h.Analyst.Answers.Enqueue(Written("الفرق 987654 يوماً"));
        h.Analyst.Answers.Enqueue(Written("ما زال 987654"));

        var result = await h.Service().RunAsync(ReportRun.Daily, Today, ReportRun.Preview, null, true, false, CancellationToken.None);

        h.Analyst.Corrections.Should().HaveCount(2);
        h.Analyst.Corrections[1].Should().Contain("987654");
        result!.Run.Analysis.Should().Be(ReportRun.AnalysisFailed);
        result.Run.InputTokens.Should().Be(24_000, "both calls were paid for and both count towards the cap");
        h.Renderer.Calls.Should().OnlyContain(c => c.Analysis == null);
    }

    [Fact]
    public async Task Run_WithoutAnApiKey_MakesTheReportWithoutAnalysis()
    {
        var h = new Harness();
        h.Settings.AnthropicApiKey = null;

        var result = await h.Service().RunAsync(ReportRun.Weekly, new DateOnly(2026, 11, 5), ReportRun.Preview, null, true, false, CancellationToken.None);

        result!.Run.Analysis.Should().Be(ReportRun.AnalysisNotConfigured);
        result.Run.Status.Should().Be(ReportRun.Done);
    }

    [Fact]
    public void MonthStart_IsMidnightSaudiTimeOnTheFirst()
    {
        StakeholderReportService.MonthStartUtc(new DateTime(2026, 11, 30, 22, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc), "at 01:00 Saudi time on 1 December a new month has begun");
    }

    // ------------------------------------------------------------------ the page

    [Theory]
    [InlineData("daily", false)]
    [InlineData("weekly", true)]
    [InlineData("monthly", false)]
    public void Renderer_DrawsEveryKind_WithAndWithoutAnAnalysis(string kind, bool phone)
    {
        var to = kind == ReportRun.Weekly ? new DateOnly(2026, 11, 5) : kind == ReportRun.Monthly ? new DateOnly(2026, 10, 31) : Today;
        var inputs = Inputs(Enumerable.Range(0, 40)
            .Select(i => to.AddDays(-i))
            .Where(StakeholderReportBuilder.IsWorkingDay)
            .Select(d => Report(d, d.Day % 2 == 0 ? Floor2 : Floor3, 5, met: d.Day % 3 != 0, cause: d.Day % 3 == 0 ? "حرارة <شديدة>" : null))
            .ToArray());
        var report = StakeholderReportBuilder.Build(inputs, kind, to);
        var analysis = new ReportAnalysis(
            "خلاصة", new[] { new ReportFinding("60%", "warn", "عنوان", "نص") }, new ReportChartNotes("أ", "ب", "ج"),
            new[] { new ReportProjectAnalysis("119", "وضع", "سبب", "إجراء") },
            new[] { new ReportRecommendation("high", "119", "إجراء", "محمد قاسم", "غداً") }, new[] { "متابعة" });
        var renderer = new ReportHtmlRenderer();

        var withAnalysis = renderer.RenderHtml(report, analysis, null, phone);
        var without = renderer.RenderHtml(report, null, "لا تحليل", phone);

        withAnalysis.Should().Contain("dir=\"rtl\"").And.Contain("التوصيات").And.Contain("نزل الأخيار").And.Contain(phone ? "class=\"phone\"" : "class=\"a4\"");
        withAnalysis.Should().Contain("حرارة &lt;شديدة&gt;", "text from the forms is escaped");
        without.Should().Contain("لا تحليل").And.NotContain("<h2>التوصيات</h2>");
    }
}
