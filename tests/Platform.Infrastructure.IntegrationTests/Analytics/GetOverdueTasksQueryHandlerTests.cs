using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics;
using Platform.Application.Analytics.Queries.GetOverdueTasks;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetOverdueTasksQueryHandlerTests
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
    public async Task Handle_AppliesTheExactOverdueDefinition_NotCompletedBookkeeping()
    {
        await using var db = CreateContext();
        var taskForm = CreatePublishedForm("task-tracking", "Operations", "Data_TaskTracking");
        var projectsForm = CreatePublishedForm("projects", "Operations", "Data_Projects");
        db.FormDefinitions.AddRange(taskForm, projectsForm);
        await db.SaveChangesAsync();

        var projectId = Guid.NewGuid();
        var fakeDynamicData = new FakeDynamicDataRepository();
        fakeDynamicData.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(projectId, new Dictionary<string, object?> { ["project_code"] = "PRJ-014", ["project_name"] = "Al Waha Towers" }),
        };

        var now = DateTime.UtcNow;
        var overdueNotCompleted = Guid.NewGuid();
        var overdueButCompleted = Guid.NewGuid(); // status says completed - must be excluded even though due_date is in the past
        var notYetDue = Guid.NewGuid();
        var noDueDateAtAll = Guid.NewGuid();
        var blockedAndOverdue = Guid.NewGuid(); // any non-completed status counts, not just "in_progress"

        var fakeExecutiveOverview = new FakeExecutiveOverviewRepository();
        fakeExecutiveOverview.TaskRows["Data_TaskTracking"] = new List<TaskTrackingRow>
        {
            new(overdueNotCompleted, projectId, "TSK-1", "Overdue, in progress", "QA Team", "high", "in_progress", now.AddDays(-5)),
            new(overdueButCompleted, projectId, "TSK-2", "Overdue but done", "QA Team", "high", "completed", now.AddDays(-10)),
            new(notYetDue, projectId, "TSK-3", "Due next week", "QA Team", "medium", "not_started", now.AddDays(7)),
            new(noDueDateAtAll, projectId, "TSK-4", "No due date set", "QA Team", "low", "not_started", null),
            new(blockedAndOverdue, projectId, "TSK-5", "Blocked and overdue", "Procurement", "urgent", "blocked", now.AddDays(-1)),
        };

        var handler = new GetOverdueTasksQueryHandler(db, fakeDynamicData, fakeExecutiveOverview);
        var result = await handler.Handle(new GetOverdueTasksQuery(), CancellationToken.None);

        result.Select(r => r.TaskId).Should().BeEquivalentTo(new[] { overdueNotCompleted, blockedAndOverdue },
            "only the two tasks with a past due_date AND a non-completed status should be reported");

        // Oldest due date (most overdue) first.
        result.Select(r => r.TaskId).Should().ContainInOrder(overdueNotCompleted, blockedAndOverdue);

        result.Single(r => r.TaskId == overdueNotCompleted).ProjectCode.Should().Be("PRJ-014");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenTaskTrackingFormDoesNotExist()
    {
        await using var db = CreateContext();
        var handler = new GetOverdueTasksQueryHandler(db, new FakeDynamicDataRepository(), new FakeExecutiveOverviewRepository());

        var result = await handler.Handle(new GetOverdueTasksQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
