# Editing published forms + form deletion — integration checklist

Two new, self-contained files:
- `src/Platform.Application/Forms/Commands/StartNewFormVersion/StartNewFormVersionCommand.cs`
- `src/Platform.Application/Forms/Commands/DeleteForm/DeleteFormCommand.cs`

No DI changes, no new NuGet packages - both go through `IApplicationDbContext` only.

## Addition to `FormsController.cs`

```csharp
[HttpPost("{id:guid}/versions")]
public async Task<IActionResult> StartNewVersion(Guid id, CancellationToken cancellationToken)
{
    var newVersionId = await _sender.Send(new StartNewFormVersionCommand(id), cancellationToken);
    return Ok(new { id = newVersionId });
}

[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
{
    await _sender.Send(new DeleteFormCommand(id), cancellationToken);
    return NoContent();
}
```

Add these usings:
```csharp
using Platform.Application.Forms.Commands.StartNewFormVersion;
using Platform.Application.Forms.Commands.DeleteForm;
```

## Verification

1. `dotnet build`. No migration needed - both commands only touch existing tables.

2. **Editing a published form** - the real test: pick an already-published form with
   submitted data (Materials is a good choice, has real rows). `POST .../versions`, confirm
   the response's `id` is a new version, then `GET /api/forms/{id}` and confirm `status` is
   back to `Draft` and the field list matches what was published before (carried forward
   correctly). Add one genuinely new field via the existing `POST .../fields`, publish
   again via the existing endpoint, then:
   - Confirm via SQL that the physical table now has the new column, and every *existing*
     row has `NULL` in it (expected - old rows predate the field, nothing retroactively
     fills them in).
   - Submit a *new* record with the new field populated, confirm it's stored correctly.
   - This is the specific thing worth watching closely: confirm the EF tracking gotcha
     doesn't show up here. Add a field to the new draft, then check directly in SQL that
     the new `FieldDefinition` row actually exists - not just that the API call returned
     success. See the comment in `StartNewFormVersionCommandHandler` for why this
     specific spot is worth the extra scrutiny even though the reasoning suggests it's fine.

3. **Deletion, three cases**, each confirmed via SQL, not just the API response:
   - Delete a draft form that was never published → the `FormDefinitions` row (and its
     `FormVersions`/`FieldDefinitions`) should be genuinely gone, not just soft-deleted.
   - Delete a published form with no Lookups pointing at it and no workflow → confirm
     `IsDeleted = 1` in SQL, but the physical `Data_*` table and its rows are still there,
     untouched. Confirm `GET /api/forms` no longer lists it.
   - Try deleting Materials or Locations (both are Lookup targets for other forms) →
     confirm this fails with a `400` naming which forms reference it, not a raw exception.
   - Try deleting Stock Adjustment (has a workflow attached) → confirm this fails with the
     workflow-specific message.
