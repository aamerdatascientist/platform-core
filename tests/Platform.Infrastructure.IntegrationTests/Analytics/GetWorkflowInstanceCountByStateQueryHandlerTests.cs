using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Queries.GetWorkflowInstanceCountByState;
using Platform.Application.Common.Exceptions;
using Platform.Domain.Workflow;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// Unit tests against EF Core's InMemory provider, not a real Postgres/SQL Server engine -
/// unlike DynamicSchemaServiceTests, these handlers are ordinary LINQ-to-Entities queries
/// over the static schema, not hand-built provider-specific SQL text, so InMemory is enough
/// to prove the query logic without needing Docker (which doesn't work in every dev/CI
/// environment for this project - see CLAUDE.md).
/// </summary>
public class GetWorkflowInstanceCountByStateQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Handle_IncludesStatesWithNoLiveInstances_AtZero()
    {
        await using var db = CreateContext();

        var workflowId = Guid.NewGuid();
        var submitted = WorkflowState.Create(workflowId, "submitted", "Submitted", true, false);
        var underReview = WorkflowState.Create(workflowId, "under_review", "Under Review", false, false);
        var approved = WorkflowState.Create(workflowId, "approved", "Approved", false, true);
        var rejected = WorkflowState.Create(workflowId, "rejected", "Rejected", false, true);
        db.WorkflowStates.AddRange(submitted, underReview, approved, rejected);

        var formId = Guid.NewGuid();
        // 3 instances sitting in Submitted, 2 in Under Review, 1 in Rejected, none in
        // Approved - Approved must still come back in the result, at count 0, not be
        // silently omitted.
        db.WorkflowInstances.AddRange(
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), submitted.Id, Guid.NewGuid()),
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), submitted.Id, Guid.NewGuid()),
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), submitted.Id, Guid.NewGuid()),
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), underReview.Id, Guid.NewGuid()),
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), underReview.Id, Guid.NewGuid()),
            WorkflowInstance.Start(workflowId, formId, Guid.NewGuid(), rejected.Id, Guid.NewGuid()));

        // A second, unrelated workflow definition with an instance in its own "submitted"
        // state - proves the query is scoped by WorkflowDefinitionId and doesn't leak
        // another workflow's counts into this one's results.
        var otherWorkflowId = Guid.NewGuid();
        var otherState = WorkflowState.Create(otherWorkflowId, "submitted", "Submitted", true, false);
        db.WorkflowStates.Add(otherState);
        db.WorkflowInstances.Add(
            WorkflowInstance.Start(otherWorkflowId, Guid.NewGuid(), Guid.NewGuid(), otherState.Id, Guid.NewGuid()));

        await db.SaveChangesAsync();

        var handler = new GetWorkflowInstanceCountByStateQueryHandler(db);
        var result = await handler.Handle(new GetWorkflowInstanceCountByStateQuery(workflowId), CancellationToken.None);

        result.Should().HaveCount(4, "all four states of the workflow should be represented");

        result.Should().ContainSingle(s => s.StateCode == "approved")
            .Which.InstanceCount.Should().Be(0);

        // Ordered descending by count.
        result.Select(s => s.StateCode).Should().ContainInOrder("submitted", "under_review", "rejected", "approved");

        result.Single(s => s.StateCode == "submitted").InstanceCount.Should().Be(3);
        result.Single(s => s.StateCode == "under_review").InstanceCount.Should().Be(2);
        result.Single(s => s.StateCode == "rejected").InstanceCount.Should().Be(1);
        result.Single(s => s.StateCode == "rejected").IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ThrowsNotFound_WhenWorkflowDefinitionHasNoStates()
    {
        await using var db = CreateContext();

        var handler = new GetWorkflowInstanceCountByStateQueryHandler(db);
        var act = () => handler.Handle(new GetWorkflowInstanceCountByStateQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
