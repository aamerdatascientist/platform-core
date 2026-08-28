using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics;
using Platform.Application.Analytics.Queries.GetCrewCountByProject;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// The Riyadh-day bucketing itself lives in ExecutiveOverviewRepository's SQL (a Postgres
/// AT TIME ZONE conversion) - this sandbox has no way to run that against a real engine (no
/// Docker, see CLAUDE.md), so these tests cover what's actually in this handler: resolving
/// Labor Log's headcount rows against Project display names, and using headcount summed
/// here rather than Daily Site Report's separately-typed crew_count.
/// </summary>
public class GetCrewCountByProjectQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FormDefinition CreatePublishedForm(string code, string moduleName, string tableName)
    {
        var form = FormDefinition.Create(code, code, moduleName, null);
        var draft = form.GetDraftVersion();
        draft.AddField("placeholder", "Placeholder", FieldType.ShortText, false, null, null, null);
        draft.MarkPublished();
        form.MarkPublished(draft, tableName);
        return form;
    }

    [Fact]
    public async Task Handle_ResolvesProjectNames_AndOrdersByMostRecentDateFirst()
    {
        await using var db = CreateContext();
        db.FormDefinitions.AddRange(
            CreatePublishedForm("labor-log", "Operations", "Data_LaborLog"),
            CreatePublishedForm("projects", "Operations", "Data_Projects"));
        await db.SaveChangesAsync();

        var wahaId = Guid.NewGuid();
        var nakheelId = Guid.NewGuid();
        var deletedProjectId = Guid.NewGuid(); // referenced by a log row, but not in the (soft-delete-filtered) master data fetch

        var fakeDynamicData = new FakeDynamicDataRepository();
        fakeDynamicData.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(wahaId, new Dictionary<string, object?> { ["project_code"] = "PRJ-014", ["project_name"] = "Al Waha Towers" }),
            new(nakheelId, new Dictionary<string, object?> { ["project_code"] = "PRJ-021", ["project_name"] = "Al Nakheel Complex" }),
        };

        var older = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);

        var fakeExecutiveOverview = new FakeExecutiveOverviewRepository();
        fakeExecutiveOverview.CrewCounts["Data_LaborLog"] = new List<ProjectDateHeadcountRow>
        {
            new(wahaId, older, 20), // steel fixers (12) + carpenters (8) already summed by the repository
            new(nakheelId, newer, 6),
            new(deletedProjectId, newer, 3),
        };

        var handler = new GetCrewCountByProjectQueryHandler(db, fakeDynamicData, fakeExecutiveOverview);
        var result = await handler.Handle(new GetCrewCountByProjectQuery(), CancellationToken.None);

        result.Should().HaveCount(3);
        result.Single(r => r.ProjectId == wahaId).Should().BeEquivalentTo(
            new { ProjectCode = "PRJ-014", ProjectName = "Al Waha Towers", TotalHeadcount = 20 });

        // Most recent date first.
        result.Select(r => r.LogDate).Should().BeInDescendingOrder();

        // A project no longer in the master-data fetch (soft-deleted, or never resolved)
        // degrades to blank display text rather than being dropped or throwing.
        result.Single(r => r.ProjectId == deletedProjectId).ProjectCode.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenLaborLogFormDoesNotExist()
    {
        await using var db = CreateContext();
        var handler = new GetCrewCountByProjectQueryHandler(
            db, new FakeDynamicDataRepository(), new FakeExecutiveOverviewRepository());

        var result = await handler.Handle(new GetCrewCountByProjectQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
