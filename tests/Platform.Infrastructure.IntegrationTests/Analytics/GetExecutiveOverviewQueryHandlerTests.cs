using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// The handler's own job - finding the forms, reading their loosely-typed rows and turning
/// them into the calculator's input. The arithmetic itself is covered by
/// ExecutiveOverviewCalculatorTests.
/// </summary>
public class GetExecutiveOverviewQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task PublishFormAsync(ApplicationDbContext db, string code, string tableName)
    {
        var form = FormDefinition.Create(code, code, "Test", null);
        var draft = form.GetDraftVersion();
        draft.AddField("placeholder", "Placeholder", FieldType.ShortText, false, null, null, null);
        draft.MarkPublished();
        form.MarkPublished(draft, tableName);
        db.FormDefinitions.Add(form);
        await db.SaveChangesAsync();
    }

    /// <summary>A Saudi-local calendar date as the database stores it: that local midnight, in UTC.</summary>
    private static DateTime StoredDate(DateOnly localDate) =>
        DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue).AddHours(-3), DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ReturnsAnEmptyPage_WhenNoneOfTheFormsExist()
    {
        await using var db = CreateContext();
        var handler = new GetExecutiveOverviewQueryHandler(db, new FakeDynamicDataRepository());

        var result = await handler.Handle(new GetExecutiveOverviewQuery(), CancellationToken.None);

        result.Projects.Should().BeEmpty();
        result.ProjectsWithoutReports.Should().Be(0);
        result.StockByMaterial.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReadsTheForms_AndPlacesEachReportOnItsSaudiLocalDate()
    {
        await using var db = CreateContext();
        await PublishFormAsync(db, "projects", "Data_Projects");
        await PublishFormAsync(db, "project-zones", "Data_ProjectZones");
        await PublishFormAsync(db, "daily-progress-report", "Data_DailyProgressReport");
        await PublishFormAsync(db, "stock-inflow", "Data_StockInflow");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var projectId = Guid.NewGuid();
        var idleProjectId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        var fake = new FakeDynamicDataRepository();
        fake.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(projectId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-1", ["project_name"] = "Tower", ["city"] = "Riyadh", ["status"] = "active",
                ["project_manager"] = null,
                ["start_date"] = StoredDate(today.AddDays(-10)), ["expected_completion"] = StoredDate(today.AddDays(30)),
            }),
            new(idleProjectId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-2", ["project_name"] = "No reports yet", ["status"] = "planning",
                ["start_date"] = null, ["expected_completion"] = null,
            }),
        };
        fake.Rows["Data_ProjectZones"] = new List<DynamicRow>
        {
            new(zoneId, new Dictionary<string, object?> { ["project"] = projectId, ["zone_code"] = "zone_1" }),
            new(Guid.NewGuid(), new Dictionary<string, object?> { ["project"] = projectId, ["zone_code"] = "zone_2" }),
        };
        fake.Rows["Data_DailyProgressReport"] = new List<DynamicRow>
        {
            new(Guid.NewGuid(), new Dictionary<string, object?>
            {
                ["report_date"] = StoredDate(today), ["project"] = projectId, ["crew_count"] = 12, ["weather"] = "clear",
                ["had_delay"] = true, ["delay_cause"] = "Late delivery", ["phase"] = "excavation", ["zone"] = zoneId,
                ["milestone"] = null, ["has_problem_today"] = false, ["problem_description"] = null,
                ["plan_completed_today"] = true,
            }),
            // No project: can't be placed anywhere, must be skipped rather than break the page.
            new(Guid.NewGuid(), new Dictionary<string, object?>
            {
                ["report_date"] = StoredDate(today), ["project"] = null, ["crew_count"] = 50, ["phase"] = "excavation",
                ["plan_completed_today"] = true,
            }),
        };
        fake.Rows["Data_StockInflow"] = new List<DynamicRow>
        {
            new(Guid.NewGuid(), new Dictionary<string, object?>
            {
                ["date"] = StoredDate(today), ["project"] = projectId, ["material"] = "other",
                ["material_other"] = "Cables", ["quantity"] = 20.5m, ["unit"] = "roll",
            }),
        };

        var handler = new GetExecutiveOverviewQueryHandler(db, fake);
        var result = await handler.Handle(new GetExecutiveOverviewQuery(), CancellationToken.None);

        result.ProjectsWithoutReports.Should().Be(1);
        var project = result.Projects.Single();
        project.Code.Should().Be("PRJ-1");
        project.ReportCount.Should().Be(1);
        project.LastReportDate.Should().Be(today, "a date stored as 21:00 UTC the evening before is that Saudi-local day");
        project.DaysSinceLastReport.Should().Be(0);
        project.TimeElapsed.Should().BeApproximately(0.25, 1e-9);
        // 1 met day on a 2-day zone; 2 zones x 2 days + 10 paperwork days planned.
        project.WorkComplete.Should().BeApproximately(1d / 14d, 1e-9);

        result.Tiles.WorkersOnSite.Should().Be(12);
        result.DelayCauses.Single().Label.Should().Be("Late delivery");

        var material = result.StockByMaterial.Single();
        material.Material.Should().Be("Cables", "the text typed into the 'other' box is the real material");
        material.IsCustomMaterial.Should().BeTrue();
        material.Received.Should().Be(20.5);
    }
}
