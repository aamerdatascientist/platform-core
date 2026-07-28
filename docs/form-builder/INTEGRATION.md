# Form Builder backend — integration checklist

One new self-contained file:
`src/Platform.Application/Forms/Commands/RemoveField/RemoveFieldCommand.cs`

## Before anything else — check this first

Your bug-fix history mentioned "StartNewDraftVersion" as something already fixed. I don't
know if that means a command for adding fields to an already-published form already
exists in this repo. Check for a file resembling `StartNewFormVersionCommand` or similar
under `src/Platform.Application/Forms/Commands/` before assuming it's missing. If it
exists, the Form Builder UI's "this form is published, read-only" limitation (see the
frontend README) is a known gap the frontend chose not to build around yet, not something
blocked by the backend - worth a follow-up round to wire the UI to it, not this one.

## One small addition needed: `FormVersion.RemoveField`

The domain entity doesn't have a remove method yet - `DeactivateField` exists (for
published versions, preserves the physical column) but full removal for **draft-only**
fields (never published, no physical column exists yet, so there's no historical data to
protect) doesn't. Add this method to `src/Platform.Domain/Forms/FormVersion.cs`, alongside
the existing `AddField`/`DeactivateField`:

```csharp
public void RemoveField(Guid fieldId)
{
    if (Status != FormStatus.Draft)
        throw new InvalidOperationException("Fields can only be removed from a draft version.");

    var removed = _fields.RemoveAll(f => f.Id == fieldId);
    if (removed == 0)
        throw new InvalidOperationException("Field not found on this version.");
}
```

## One addition to `FormsController.cs`

```csharp
[HttpDelete("{id:guid}/fields/{fieldId:guid}")]
public async Task<IActionResult> RemoveField(Guid id, Guid fieldId, CancellationToken cancellationToken)
{
    await _sender.Send(new RemoveFieldCommand(id, fieldId), cancellationToken);
    return NoContent();
}
```

Add `using Platform.Application.Forms.Commands.RemoveField;` to the top of the file.

## Verification

1. `dotnet build`. No migration needed - this only removes a row from the metadata
   table, no physical dynamic-table columns are touched (draft fields never get one).
2. Real test: create a form, add two fields, `DELETE` one of them by ID, confirm
   `GET /api/forms/{id}` now shows only the remaining field. Then publish, and confirm
   `DELETE` on a field of a *published* form correctly fails (400/500 with the
   "only a draft version" message) - that's the safety check working, not a bug.
