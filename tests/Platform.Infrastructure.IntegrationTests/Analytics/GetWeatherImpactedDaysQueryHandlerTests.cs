using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics;
using Platform.Application.Analytics.Queries.GetWeatherImpactedDays;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetWeatherImpactedDaysQueryHandlerTests
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
    public async Task Handle_CountsEveryNonClearValueAsImpacted_IncludingOther()
    {
        await using var db = CreateContext();
        db.FormDefinitions.AddRange(
            CreatePublishedForm("daily-site-report", "Operations", "Data_DailySiteReport"),
            CreatePublishedForm("projects", "Operations", "Data_Projects"));
        await db.SaveChangesAsync();

        var wahaId = Guid.NewGuid();
        var fakeDynamicData = new FakeDynamicDataRepository();
        fakeDynamicData.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(wahaId, new Dictionary<string, object?> { ["project_code"] = "PRJ-014", ["project_name"] = "Al Waha Towers" }),
        };

        var fakeExecutiveOverview = new FakeExecutiveOverviewRepository();
        fakeExecutiveOverview.WeatherCounts["Data_DailySiteReport"] = new List<ProjectWeatherCountRow>
        {
            new(wahaId, "clear", 10),
            new(wahaId, "extreme_heat", 3),
            new(wahaId, "sandstorm", 1),
            new(wahaId, "other", 2), // unresolved/unknown weather still counts as "impacted" for this KPI
        };

        var handler = new GetWeatherImpactedDaysQueryHandler(db, fakeDynamicData, fakeExecutiveOverview);
        var result = await handler.Handle(new GetWeatherImpactedDaysQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        var summary = result.Single();
        summary.TotalDays.Should().Be(16);
        summary.ImpactedDays.Should().Be(6, "everything except 'clear' counts - 3 + 1 + 2");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenDailySiteReportFormDoesNotExist()
    {
        await using var db = CreateContext();
        var handler = new GetWeatherImpactedDaysQueryHandler(
            db, new FakeDynamicDataRepository(), new FakeExecutiveOverviewRepository());

        var result = await handler.Handle(new GetWeatherImpactedDaysQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
