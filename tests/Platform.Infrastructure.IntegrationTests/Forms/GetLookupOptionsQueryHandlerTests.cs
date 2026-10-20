using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Queries.GetLookupOptions;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// The Lookup choices a form-filler gets: Id + label only, from the target form's first
/// ShortText field, narrowed by the sibling value for a cascading Lookup.
/// </summary>
public class GetLookupOptionsQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Setup(
        ApplicationDbContext Db, FakeDynamicDataRepository Data, FormDefinition Report,
        Guid ProjectFieldId, Guid ZoneFieldId, Guid NotesFieldId, Guid ProjectA, Guid ProjectB);

    /// <summary>projects + project-zones (Lookup to projects) + a report with a Project Lookup
    /// and a Zone Lookup filtered by project - the live Daily Progress Report's shape.</summary>
    private static async Task<Setup> CreateAsync()
    {
        var db = CreateContext();

        var projects = FormDefinition.Create("projects", "Projects", "Operations", null);
        var pv = projects.GetDraftVersion();
        pv.AddField("project_code", "Code", FieldType.ShortText, true, null, null, null);
        pv.AddField("project_name", "Name", FieldType.ShortText, true, null, null, null);
        pv.AddField("notes", "Notes", FieldType.LongText, false, null, null, null);
        pv.MarkPublished();
        projects.MarkPublished(pv, "Data_Projects");

        var zones = FormDefinition.Create("project-zones", "Zones", "Operations", null);
        var zv = zones.GetDraftVersion();
        zv.AddField("project", "Project", FieldType.Lookup, true, null, projects.Id, null);
        zv.AddField("zone_code", "Zone", FieldType.ShortText, true, null, null, null);
        zv.MarkPublished();
        zones.MarkPublished(zv, "Data_ProjectZones");

        var report = FormDefinition.Create("daily-progress-report", "Daily report", "Daily Reports", null);
        var rv = report.GetDraftVersion();
        var projectField = rv.AddField("project", "Project", FieldType.Lookup, true, null, projects.Id, null);
        var zoneField = rv.AddField("zone", "Zone", FieldType.Lookup, false, null, zones.Id, null, filterByFieldCode: "project");
        var notesField = rv.AddField("notes", "Notes", FieldType.LongText, false, null, null, null);
        rv.MarkPublished();
        report.MarkPublished(rv, "Data_DailyProgressReport");

        db.FormDefinitions.AddRange(projects, zones, report);
        await db.SaveChangesAsync();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var data = new FakeDynamicDataRepository();
        data.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(a, new Dictionary<string, object?> { ["project_code"] = "PRJ-A", ["project_name"] = "Tower", ["notes"] = "private" }),
            new(b, new Dictionary<string, object?> { ["project_code"] = null, ["project_name"] = "Mall", ["notes"] = null }),
        };
        data.Rows["Data_ProjectZones"] = new List<DynamicRow>
        {
            new(Guid.NewGuid(), new Dictionary<string, object?> { ["project"] = a, ["zone_code"] = "zone_1" }),
            new(Guid.NewGuid(), new Dictionary<string, object?> { ["project"] = a, ["zone_code"] = "zone_2" }),
            new(Guid.NewGuid(), new Dictionary<string, object?> { ["project"] = b, ["zone_code"] = "zone_9" }),
        };

        return new Setup(db, data, report, projectField.Id, zoneField.Id, notesField.Id, a, b);
    }

    [Fact]
    public async Task Handle_ReturnsEveryTargetRow_LabelledByItsFirstShortTextField()
    {
        var s = await CreateAsync();
        var handler = new GetLookupOptionsQueryHandler(s.Db, s.Data);

        var result = await handler.Handle(new GetLookupOptionsQuery(s.Report.Id, s.ProjectFieldId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(o => o.Id == s.ProjectA).Label.Should().Be("PRJ-A");
        result.Single(o => o.Id == s.ProjectB).Label.Should().Be(s.ProjectB.ToString(),
            "a row with no value in the label field falls back to its Id, same as the form did before");
    }

    [Fact]
    public async Task Handle_NarrowsACascadingLookup_ToTheSiblingValue()
    {
        var s = await CreateAsync();
        var handler = new GetLookupOptionsQueryHandler(s.Db, s.Data);

        var result = await handler.Handle(
            new GetLookupOptionsQuery(s.Report.Id, s.ZoneFieldId, s.ProjectA.ToString()), CancellationToken.None);

        result.Select(o => o.Label).Should().BeEquivalentTo("zone_1", "zone_2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Handle_ReturnsNothing_ForACascadingLookupWithoutAUsableSiblingValue(string? filterValue)
    {
        var s = await CreateAsync();
        var handler = new GetLookupOptionsQueryHandler(s.Db, s.Data);

        var result = await handler.Handle(new GetLookupOptionsQuery(s.Report.Id, s.ZoneFieldId, filterValue), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsNothing_ForAFieldThatIsNotALookup()
    {
        var s = await CreateAsync();
        var handler = new GetLookupOptionsQueryHandler(s.Db, s.Data);

        var result = await handler.Handle(new GetLookupOptionsQuery(s.Report.Id, s.NotesFieldId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReadsEveryPage_WhenTheTargetHasMoreRowsThanOnePage()
    {
        var s = await CreateAsync();
        s.Data.Rows["Data_Projects"] = Enumerable.Range(1, 450)
            .Select(i => new DynamicRow(Guid.NewGuid(), new Dictionary<string, object?> { ["project_code"] = $"P{i}" }))
            .ToList();
        var handler = new GetLookupOptionsQueryHandler(s.Db, s.Data);

        var result = await handler.Handle(new GetLookupOptionsQuery(s.Report.Id, s.ProjectFieldId), CancellationToken.None);

        result.Should().HaveCount(450);
    }

    [Fact]
    public void Query_IsFormScoped_SoAccessIsCheckedAgainstTheFormBeingFilled()
    {
        typeof(IFormScopedRequest).IsAssignableFrom(typeof(GetLookupOptionsQuery)).Should().BeTrue();
    }
}
