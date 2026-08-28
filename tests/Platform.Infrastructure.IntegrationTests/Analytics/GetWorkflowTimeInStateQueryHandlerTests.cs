using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Queries.GetWorkflowTimeInState;
using Platform.Domain.Workflow;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetWorkflowTimeInStateQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// WorkflowInstanceHistoryEntry.Create always stamps ExecutedAtUtc as DateTime.UtcNow at
    /// the moment it's called (no way to pass an explicit timestamp through the domain API,
    /// by design - see the entry's Create factory). Realistic multi-day fixture data needs
    /// deterministic past timestamps, so this reaches past the private setter the same way
    /// EF Core itself does internally - Entry(...).Property(...).CurrentValue bypasses C#
    /// accessibility, unlike reflection on the CLR property directly.
    /// </summary>
    private static void SetExecutedAt(ApplicationDbContext db, WorkflowInstanceHistoryEntry entry, DateTime executedAtUtc) =>
        db.Entry(entry).Property(nameof(WorkflowInstanceHistoryEntry.ExecutedAtUtc)).CurrentValue = executedAtUtc;

    [Fact]
    public async Task Handle_ComputesInProgressStateDurationAgainstNow_AndCompletedTransitionsExactly()
    {
        await using var db = CreateContext();

        var workflowId = Guid.NewGuid();
        var submitted = WorkflowState.Create(workflowId, "submitted", "Submitted", true, false);
        var underReview = WorkflowState.Create(workflowId, "under_review", "Under Review", false, false);
        var approved = WorkflowState.Create(workflowId, "approved", "Approved", false, true);
        db.WorkflowStates.AddRange(submitted, underReview, approved);

        var formId = Guid.NewGuid();

        // Instance A: a completed multi-step path, entirely in the past.
        // Submitted at T0 -> Under Review at T0+2h -> Approved (final) at T0+7h.
        var t0 = DateTime.UtcNow.AddDays(-10);
        var instanceA = WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), submitted.Id, Guid.NewGuid());
        var toUnderReview = instanceA.ApplyTransition(Guid.NewGuid(), underReview.Id, Guid.NewGuid(), "moved to review");
        var toApproved = instanceA.ApplyTransition(Guid.NewGuid(), approved.Id, Guid.NewGuid(), "approved");

        // Instance B: still sitting in its very first state, days ago - the in-progress
        // case the handler has to measure against "now", not just completed transitions.
        var t0B = DateTime.UtcNow.AddDays(-3);
        var instanceB = WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), submitted.Id, Guid.NewGuid());

        // Add the fully-built graphs in one shot each - both instances are entirely new
        // here, never previously tracked, so EF's Add() correctly walks and inserts every
        // history entry reachable through the History navigation. Adding new history
        // entries to an ALREADY-tracked instance later (e.g. via a second ApplyTransition
        // after this Add) would hit the "child reached only through an already-tracked
        // parent's navigation collection" gotcha documented in CLAUDE.md, and needs an
        // explicit db.Set<WorkflowInstanceHistoryEntry>().Add(...) instead.
        db.WorkflowInstances.AddRange(instanceA, instanceB);

        SetExecutedAt(db, instanceA.History.First(h => h.ToStateId == submitted.Id), t0);
        SetExecutedAt(db, toUnderReview, t0.AddHours(2));
        SetExecutedAt(db, toApproved, t0.AddHours(7));
        SetExecutedAt(db, instanceB.History.Single(), t0B);

        await db.SaveChangesAsync();

        var handler = new GetWorkflowTimeInStateQueryHandler(db);
        var beforeCall = DateTime.UtcNow;
        var result = await handler.Handle(new GetWorkflowTimeInStateQuery(workflowId), CancellationToken.None);
        var afterCall = DateTime.UtcNow;

        result.Should().HaveCount(2);

        var resultA = result.Single(r => r.WorkflowInstanceId == instanceA.Id);
        resultA.StateDurations.Should().HaveCount(3);

        resultA.StateDurations[0].StateCode.Should().Be("submitted");
        resultA.StateDurations[0].EnteredAtUtc.Should().Be(t0);
        resultA.StateDurations[0].ExitedAtUtc.Should().Be(t0.AddHours(2));
        resultA.StateDurations[0].Duration.Should().Be(TimeSpan.FromHours(2));

        resultA.StateDurations[1].StateCode.Should().Be("under_review");
        resultA.StateDurations[1].EnteredAtUtc.Should().Be(t0.AddHours(2));
        resultA.StateDurations[1].ExitedAtUtc.Should().Be(t0.AddHours(7));
        resultA.StateDurations[1].Duration.Should().Be(TimeSpan.FromHours(5));

        // Approved is final, but it's still the "current" state - the instance has been
        // sitting in it since t0+7h, so this must be measured against now, not treated as
        // zero/complete just because the state is terminal.
        var approvedDuration = resultA.StateDurations[2];
        approvedDuration.StateCode.Should().Be("approved");
        approvedDuration.EnteredAtUtc.Should().Be(t0.AddHours(7));
        approvedDuration.ExitedAtUtc.Should().BeNull("the instance hasn't left this state - there's no next transition");
        approvedDuration.Duration.Should().BeGreaterThanOrEqualTo(beforeCall - t0.AddHours(7));
        approvedDuration.Duration.Should().BeLessThanOrEqualTo(afterCall - t0.AddHours(7));

        var resultB = result.Single(r => r.WorkflowInstanceId == instanceB.Id);
        resultB.StateDurations.Should().HaveCount(1);

        var inProgress = resultB.StateDurations[0];
        inProgress.StateCode.Should().Be("submitted");
        inProgress.EnteredAtUtc.Should().Be(t0B);
        inProgress.ExitedAtUtc.Should().BeNull();
        // Unambiguously a multi-day, "measured against current time" duration, not a
        // coincidentally-near-zero value that would also pass if the handler had a bug that
        // dropped the in-progress case entirely.
        inProgress.Duration.Should().BeGreaterThan(TimeSpan.FromDays(2.9));
        inProgress.Duration.Should().BeLessThanOrEqualTo(afterCall - t0B);
    }
}
