# Form access control — integration checklist

New files, self-contained:
- `src/Platform.Domain/Forms/FormDefinitionRole.cs`
- `src/Platform.Application/Forms/FormAccessChecker.cs`
- `src/Platform.Application/Forms/Commands/SetFormAllowedRoles/SetFormAllowedRolesCommand.cs`
- `src/Platform.Infrastructure/Persistence/Configurations/FormDefinitionRoleConfiguration.cs`

No new NuGet packages, no new DI registrations.

## 1. `src/Platform.Domain/Forms/FormDefinition.cs`

Add a new collection and one method, alongside the existing `_versions`:
```csharp
private readonly List<FormDefinitionRole> _allowedRoles = new();
public IReadOnlyCollection<FormDefinitionRole> AllowedRoles => _allowedRoles.AsReadOnly();

/// <summary>
/// Empty list means open to everyone - see FormAccessChecker. Returns only the newly
/// created rows, same reason every other "parent creates child" method in this codebase
/// does this - see CLAUDE.md's tracking gotcha entries, and note below.
/// </summary>
public IReadOnlyList<FormDefinitionRole> SetAllowedRoles(IEnumerable<Guid> roleIds)
{
    var requested = roleIds.Distinct().ToHashSet();
    _allowedRoles.RemoveAll(ar => !requested.Contains(ar.RoleId));

    var newlyAdded = new List<FormDefinitionRole>();
    foreach (var roleId in requested)
    {
        if (_allowedRoles.Any(ar => ar.RoleId == roleId)) continue;
        var entry = FormDefinitionRole.Create(Id, roleId);
        _allowedRoles.Add(entry);
        newlyAdded.Add(entry);
    }
    return newlyAdded;
}
```

## 2. `IApplicationDbContext.cs` and `ApplicationDbContext.cs`

Add, same pattern as every other module:
```csharp
DbSet<Platform.Domain.Forms.FormDefinitionRole> FormDefinitionRoles { get; }
```
```csharp
public DbSet<Platform.Domain.Forms.FormDefinitionRole> FormDefinitionRoles => Set<Platform.Domain.Forms.FormDefinitionRole>();
```

## 3. `GetFormsListQuery.cs` — filter the list by access

Inject `ICurrentUserService` (constructor param, alongside the existing
`IApplicationDbContext`). In the handler: `Include(f => f.AllowedRoles)` on the query,
fetch all `Roles` into an `Id -> Name` dictionary, then filter the results before
projecting to `FormSummaryDto`:
```csharp
var forms = await query.OrderBy(f => f.ModuleName).ThenBy(f => f.Name).ToListAsync(cancellationToken);
var roleNamesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

return forms
    .Where(f =>
    {
        var allowedNames = f.AllowedRoles.Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId)).Where(n => n is not null).Select(n => n!).ToList();
        return FormAccessChecker.HasAccess(allowedNames, _currentUser.Roles);
    })
    .Select(f => new FormSummaryDto(f.Id, f.Code, f.Name, f.ModuleName, f.Status))
    .ToList();
```
Add `using Platform.Application.Forms;` (for `FormAccessChecker` - it's a parent
namespace, not automatically in scope) and `using Platform.Application.Common.Interfaces;`
if not already present.

## 4. `GetFormDefinitionQuery.cs` — block direct access, expose current allowed roles

Add `Include(f => f.AllowedRoles)` to the existing query. After loading the form (same
place `NotFoundException` is thrown if it's null), resolve allowed role names the same
way as above and check access - throw `ForbiddenAccessException` if it fails. This
matters even though the list is already filtered: someone with a form ID from anywhere
else (an old link, a Lookup reference) shouldn't be able to bypass the list filter by
hitting the URL directly.

Add `AllowedRoleIds` to `FormDefinitionDto`:
```csharp
public record FormDefinitionDto(
    Guid Id, string Code, string Name, string? Description, string ModuleName,
    FormStatus Status, string? TableName, FormVersionDto? DraftVersion, FormVersionDto? PublishedVersion,
    IReadOnlyList<Guid> AllowedRoleIds);
```
(Update the constructor call at the end of the handler to include
`formDefinition.AllowedRoles.Select(ar => ar.RoleId).ToList()`.)

## 5. `SubmitFormDataCommand.cs` — block submission, not just visibility

Inject `ICurrentUserService` if not already present. After loading `formDefinition` and
before the existing required-fields/validation logic, add the same access check as
above (`Include(f => f.AllowedRoles)` on the load, resolve names, check, throw
`ForbiddenAccessException`). A form hidden from someone's list shouldn't still be
submittable if they somehow have its ID.

## 6. `FormsController.cs`

Add `[Authorize(Roles = "Administrator")]` **directly on each mutating action** -
`Create`, `AddField`, `Publish`, `RemoveField`, `StartNewVersion`, `Delete` - same
explicit per-action pattern already used on `AuthController.Register`, not a class-level
change (avoids any ambiguity about how multiple `[Authorize]` attributes combine).
Leave `Get` (single form) and the list action exactly as they are - `[Authorize]` only,
open to any authenticated user, now protected by the query-level checks above instead.

Add one new endpoint:
```csharp
public record SetAllowedRolesRequest(IReadOnlyList<Guid> RoleIds);

[HttpPut("{id:guid}/allowed-roles")]
[Authorize(Roles = "Administrator")]
public async Task<IActionResult> SetAllowedRoles(Guid id, SetAllowedRolesRequest request, CancellationToken cancellationToken)
{
    await _sender.Send(new SetFormAllowedRolesCommand(id, request.RoleIds), cancellationToken);
    return NoContent();
}
```
Add `using Platform.Application.Forms.Commands.SetFormAllowedRoles;`.

## Verification

1. `dotnet build`, then `dotnet ef migrations add AddFormAccessControl` and
   `dotnet ef database update` - this one genuinely needs a migration, new table.
2. **Confirm nothing broke first**: as an existing user, `GET /api/forms` should still
   return all 21 existing forms, unchanged - none of them have restrictions configured yet.
3. Restrict one form (e.g. Materials) to a role your test admin doesn't have - as that
   admin, confirm it disappears from `GET /api/forms`, and confirm `GET
   /api/forms/{materialsId}` now returns `403`, not the form.
4. Confirm `POST .../submissions` against that same restricted form also returns `403`
   for a user without the role - the block has to hold even with the ID in hand, not just
   in the list.
5. Give a non-Administrator user's role access to that form - confirm they can now see
   and submit to it, and confirm a *different* non-admin user still can't.
6. Confirm a non-Administrator gets a clean `403` (not a crash) trying to `POST
   /api/forms` (create) or `DELETE /api/forms/{id}`.
