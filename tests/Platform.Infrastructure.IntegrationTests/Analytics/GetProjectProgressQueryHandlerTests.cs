using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Queries.GetProjectProgress;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetProjectProgressQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // --- ComputePercentComplete: the null/negative-span clamping logic, tested directly and exhaustively ---

    [Fact]
    public void ComputePercentComplete_ReturnsNull_WhenStartDateIsMissing()
    {
        var result = GetProjectProgressQueryHandler.ComputePercentComplete(null, DateTime.UtcNow.AddDays(10), DateTime.UtcNow);
        result.Should().BeNull("a project with no start date has nothing to compute progress from");
    }

    [Fact]
    public void ComputePercentComplete_ReturnsNull_WhenExpectedCompletionIsMissing()
    {
        var result = GetProjectProgressQueryHandler.ComputePercentComplete(DateTime.UtcNow.AddDays(-10), null, DateTime.UtcNow);
        result.Should().BeNull();
    }

    [Fact]
    public void ComputePercentComplete_ReturnsNull_WhenBothDatesAreMissing()
    {
        var result = GetProjectProgressQueryHandler.ComputePercentComplete(null, null, DateTime.UtcNow);
        result.Should().BeNull();
    }

    [Fact]
    public void ComputePercentComplete_ReturnsNull_NotNegative_WhenExpectedCompletionIsBeforeStartDate()
    {
        // Nothing validates expected_completion >= start_date at submission time - a bad
        // data entry must degrade to "unavailable", not produce a negative or inverted
        // percentage that looks like real math.
        var start = DateTime.UtcNow;
        var end = start.AddDays(-5);

        var result = GetProjectProgressQueryHandler.ComputePercentComplete(start, end, DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public void ComputePercentComplete_ReturnsNull_NotDivideByZero_WhenExpectedCompletionEqualsStartDate()
    {
        var same = DateTime.UtcNow;
        var result = GetProjectProgressQueryHandler.ComputePercentComplete(same, same, DateTime.UtcNow);
        result.Should().BeNull();
    }

    [Fact]
    public void ComputePercentComplete_ComputesExactMidpoint()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc); // 10-day span
        var now = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc); // 5 days elapsed

        var result = GetProjectProgressQueryHandler.ComputePercentComplete(start, end, now);

        result.Should().Be(50m);
    }

    [Fact]
    public void ComputePercentComplete_ClampsToZero_WhenStartDateIsInTheFuture()
    {
        var start = DateTime.UtcNow.AddDays(5);
        var end = DateTime.UtcNow.AddDays(15);

        var result = GetProjectProgressQueryHandler.ComputePercentComplete(start, end, DateTime.UtcNow);

        result.Should().Be(0m, "a project that hasn't started yet is 0% complete, not negative");
    }

    [Fact]
    public void ComputePercentComplete_ClampsToHundred_WhenExpectedCompletionIsInThePast()
    {
        var start = DateTime.UtcNow.AddDays(-30);
        var end = DateTime.UtcNow.AddDays(-5); // expected completion already passed

        var result = GetProjectProgressQueryHandler.ComputePercentComplete(start, end, DateTime.UtcNow);

        result.Should().Be(100m, "an overrun project is capped at 100%, not allowed past it");
    }

    // --- ParseUtcDateTime: normalizes whatever Npgsql/Dapper hands back ---

    [Fact]
    public void ParseUtcDateTime_ReturnsNull_ForNull()
    {
        GetProjectProgressQueryHandler.ParseUtcDateTime(null).Should().BeNull();
    }

    [Fact]
    public void ParseUtcDateTime_NormalizesUnspecifiedKindDateTime_ToUtc()
    {
        var unspecified = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var result = GetProjectProgressQueryHandler.ParseUtcDateTime(unspecified);
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ParseUtcDateTime_ConvertsDateTimeOffset_ToUtcDateTime()
    {
        var offset = new DateTimeOffset(2026, 3, 1, 15, 0, 0, TimeSpan.FromHours(3)); // Riyadh local
        var result = GetProjectProgressQueryHandler.ParseUtcDateTime(offset);
        result.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    // --- Full handler: realistic multi-project fixture, including the null/negative cases end to end ---

    [Fact]
    public async Task Handle_ReturnsPerProjectProgress_WithMixedRealisticData()
    {
        await using var db = CreateContext();

        var projectsForm = FormDefinition.Create("projects", "Projects", "Operations", null);
        var draft = projectsForm.GetDraftVersion();
        draft.AddField("project_code", "Project Code", FieldType.ShortText, true, null, null, null);
        draft.MarkPublished();
        projectsForm.MarkPublished(draft, "Data_Projects");
        db.FormDefinitions.Add(projectsForm);
        await db.SaveChangesAsync();

        var onTrackId = Guid.NewGuid();
        var notStartedId = Guid.NewGuid();
        var noDatesId = Guid.NewGuid();
        var badDataId = Guid.NewGuid();

        var fakeDynamicData = new FakeDynamicDataRepository();
        fakeDynamicData.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(onTrackId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-014", ["project_name"] = "Al Waha Towers", ["status"] = "active",
                ["start_date"] = DateTime.UtcNow.AddDays(-30), ["expected_completion"] = DateTime.UtcNow.AddDays(30),
            }),
            new(notStartedId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-099", ["project_name"] = "Future Mall", ["status"] = "planning",
                ["start_date"] = DateTime.UtcNow.AddDays(10), ["expected_completion"] = DateTime.UtcNow.AddDays(100),
            }),
            new(noDatesId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-100", ["project_name"] = "Unscheduled Project", ["status"] = "planning",
                ["start_date"] = null, ["expected_completion"] = null,
            }),
            new(badDataId, new Dictionary<string, object?>
            {
                ["project_code"] = "PRJ-101", ["project_name"] = "Bad Data Project", ["status"] = "active",
                ["start_date"] = DateTime.UtcNow, ["expected_completion"] = DateTime.UtcNow.AddDays(-5),
            }),
        };

        var handler = new GetProjectProgressQueryHandler(db, fakeDynamicData);
        var result = await handler.Handle(new GetProjectProgressQuery(), CancellationToken.None);

        result.Should().HaveCount(4);

        result.Single(p => p.ProjectId == onTrackId).PercentComplete.Should().BeApproximately(50m, 1m);
        result.Single(p => p.ProjectId == notStartedId).PercentComplete.Should().Be(0m);
        result.Single(p => p.ProjectId == noDatesId).PercentComplete.Should().BeNull("no dates means \"—\" in the UI, not 0%");
        result.Single(p => p.ProjectId == badDataId).PercentComplete.Should().BeNull(
            "expected_completion before start_date is bad data, not a negative percentage");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenProjectsFormDoesNotExist()
    {
        await using var db = CreateContext();
        var handler = new GetProjectProgressQueryHandler(db, new FakeDynamicDataRepository());

        var result = await handler.Handle(new GetProjectProgressQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
