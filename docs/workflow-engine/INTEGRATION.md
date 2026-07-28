# Workflow Engine — integration checklist

New files (in this delivery) are self-contained and safe to drop straight into the repo
at matching paths under `src/`. These four existing files need precise, small additions —
written as intent + exact snippets rather than full-file replacements, since the current
state of these files has moved since the last full copy of this repo, from your own
bug fixes. Adapt file structure as needed if it's shifted; the logic below is what matters.

---

## 1. `Platform.Application/Common/Interfaces/IApplicationDbContext.cs`

Add six DbSet properties, alongside the existing Forms ones:

```csharp
DbSet<Platform.Domain.Workflow.WorkflowDefinition> WorkflowDefinitions { get; }
DbSet<Platform.Domain.Workflow.WorkflowState> WorkflowStates { get; }
DbSet<Platform.Domain.Workflow.WorkflowTransition> WorkflowTransitions { get; }
DbSet<Platform.Domain.Workflow.WorkflowTransitionRole> WorkflowTransitionRoles { get; }
DbSet<Platform.Domain.Workflow.WorkflowInstance> WorkflowInstances { get; }
DbSet<Platform.Domain.Workflow.WorkflowInstanceHistoryEntry> WorkflowInstanceHistoryEntries { get; }
```

## 2. `Platform.Infrastructure/Persistence/ApplicationDbContext.cs`

Add matching properties, same pattern as the existing ones (`=> Set<T>()`):

```csharp
public DbSet<Platform.Domain.Workflow.WorkflowDefinition> WorkflowDefinitions => Set<Platform.Domain.Workflow.WorkflowDefinition>();
public DbSet<Platform.Domain.Workflow.WorkflowState> WorkflowStates => Set<Platform.Domain.Workflow.WorkflowState>();
public DbSet<Platform.Domain.Workflow.WorkflowTransition> WorkflowTransitions => Set<Platform.Domain.Workflow.WorkflowTransition>();
public DbSet<Platform.Domain.Workflow.WorkflowTransitionRole> WorkflowTransitionRoles => Set<Platform.Domain.Workflow.WorkflowTransitionRole>();
public DbSet<Platform.Domain.Workflow.WorkflowInstance> WorkflowInstances => Set<Platform.Domain.Workflow.WorkflowInstance>();
public DbSet<Platform.Domain.Workflow.WorkflowInstanceHistoryEntry> WorkflowInstanceHistoryEntries => Set<Platform.Domain.Workflow.WorkflowInstanceHistoryEntry>();
```

No changes needed to `OnModelCreating` - `ApplyConfigurationsFromAssembly` already picks up
the new `WorkflowConfigurations.cs` automatically, same as it does for Forms.

## 3. `Platform.Application/Forms/Commands/SubmitFormData/SubmitFormDataCommand.cs`

**Intent:** after a row is successfully inserted into the dynamic table, check whether
this form has a published workflow. If it does, start a `WorkflowInstance` at that
workflow's initial state, using the new row's ID as `RecordId`. If it doesn't, nothing
changes from current behavior - most forms won't have a workflow attached, and that's fine.

Find where the handler currently returns the new row's ID after calling
`_dynamicDataRepository.InsertAsync(...)` - insert this logic right before returning:

```csharp
var newRecordId = await _dynamicDataRepository.InsertAsync(
    formDefinition.TableName, activeFields, request.Values, request.SubmittedByUserId, cancellationToken);

var publishedWorkflow = await _db.WorkflowDefinitions
    .Include(w => w.States)
    .SingleOrDefaultAsync(
        w => w.FormDefinitionId == formDefinition.Id && w.Status == Domain.Workflow.WorkflowStatus.Published,
        cancellationToken);

if (publishedWorkflow is not null)
{
    var initialState = publishedWorkflow.GetInitialState();
    var instance = Domain.Workflow.WorkflowInstance.Start(
        publishedWorkflow.Id, formDefinition.Id, newRecordId, initialState.Id, request.SubmittedByUserId);
    _db.WorkflowInstances.Add(instance);
    await _db.SaveChangesAsync(cancellationToken);
}

return newRecordId;
```

(Adjust variable names to match whatever the current handler actually calls things -
the shape above assumes the insert call's result is captured in a variable before the
`return`, which may need a small restructure if it currently returns the call directly.)

## 4. No DI changes needed

Unlike the Form Engine, Workflow doesn't need a new service interface/implementation pair -
everything here goes through `IApplicationDbContext` directly via EF Core, since workflow
state lives in the static schema, not dynamically generated tables. `AddApplication()` and
`AddInfrastructure()` pick up the new MediatR handlers and EF configuration automatically
via their existing assembly-scanning registration - nothing to add there.

## 5. Verification, once integrated

1. `dotnet build` - confirm it compiles first, before touching the database.
2. `dotnet ef migrations add AddWorkflowEngine --project src/Platform.Infrastructure --startup-project src/Platform.Api`
3. `dotnet ef database update` (same project args)
4. Real end-to-end test, same standard as Phase 1/2: create a workflow for one existing
   form (e.g. Stock Adjustment - "Draft → Pending approval → Approved/Rejected" is a
   natural fit), add states, add a transition gated to the Administrator role, publish it,
   submit a record to that form, confirm a WorkflowInstance actually started, then call
   the transition endpoint and confirm the state actually moves and history records it.
