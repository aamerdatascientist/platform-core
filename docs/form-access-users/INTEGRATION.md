# Per-user form access — integration checklist

New files:
- `src/Platform.Domain/Forms/FormDefinitionUser.cs`
- `src/Platform.Application/Forms/Commands/SetFormAllowedUsers/SetFormAllowedUsersCommand.cs`
- `src/Platform.Infrastructure/Persistence/Configurations/FormDefinitionUserConfiguration.cs`

This layers on top of last round's role-based access (`FormDefinitionRole`,
`FormAccessChecker`) rather than replacing it - a user gets access if their role is
allowed, OR they're granted access directly, OR neither list has anything configured at
all (still open to everyone by default - that rule doesn't change).

## 1. `src/Platform.Domain/Forms/FormDefinition.cs`

Add a second collection and method, alongside `_allowedRoles`/`SetAllowedRoles`:
```csharp
private readonly List<FormDefinitionUser> _allowedUsers = new();
public IReadOnlyCollection<FormDefinitionUser> AllowedUsers => _allowedUsers.AsReadOnly();

public IReadOnlyList<FormDefinitionUser> SetAllowedUsers(IEnumerable<Guid> userIds)
{
    var requested = userIds.Distinct().ToHashSet();
    _allowedUsers.RemoveAll(au => !requested.Contains(au.UserId));

    var newlyAdded = new List<FormDefinitionUser>();
    foreach (var userId in requested)
    {
        if (_allowedUsers.Any(au => au.UserId == userId)) continue;
        var entry = FormDefinitionUser.Create(Id, userId);
        _allowedUsers.Add(entry);
        newlyAdded.Add(entry);
    }
    return newlyAdded;
}
```

## 2. `src/Platform.Application/Forms/FormAccessChecker.cs` — the actual rule change

Replace the whole method - the signature changes, not just the body:
```csharp
public static class FormAccessChecker
{
    public static bool HasAccess(
        IReadOnlyCollection<string> allowedRoleNames,
        IReadOnlyCollection<Guid> allowedUserIds,
        IReadOnlyCollection<string> callerRoleNames,
        Guid? callerUserId)
    {
        if (allowedRoleNames.Count == 0 && allowedUserIds.Count == 0) return true;
        if (callerUserId.HasValue && allowedUserIds.Contains(callerUserId.Value)) return true;
        return callerRoleNames.Any(r => allowedRoleNames.Contains(r, StringComparer.OrdinalIgnoreCase));
    }
}
```
This breaks all three existing call sites on purpose - the compiler will point at exactly
where they need updating. See below for what each one needs.

## 3. `IApplicationDbContext.cs` and `ApplicationDbContext.cs`

```csharp
DbSet<Platform.Domain.Forms.FormDefinitionUser> FormDefinitionUsers { get; }
```
```csharp
public DbSet<Platform.Domain.Forms.FormDefinitionUser> FormDefinitionUsers => Set<Platform.Domain.Forms.FormDefinitionUser>();
```

## 4. `GetFormsListQuery.cs`

Add `Include(f => f.AllowedUsers)` alongside the existing `Include(f => f.AllowedRoles)`.
Update the `HasAccess` call to the new four-argument signature:
```csharp
FormAccessChecker.HasAccess(
    allowedNames,
    f.AllowedUsers.Select(au => au.UserId).ToList(),
    _currentUser.Roles,
    _currentUser.UserId)
```

## 5. `GetFormDefinitionQuery.cs`

Same `Include` and `HasAccess` call update as above. Also add `AllowedUserIds` to the DTO:
```csharp
public record FormDefinitionDto(
    Guid Id, string Code, string Name, string? Description, string ModuleName,
    FormStatus Status, string? TableName, FormVersionDto? DraftVersion, FormVersionDto? PublishedVersion,
    IReadOnlyList<Guid> AllowedRoleIds, IReadOnlyList<Guid> AllowedUserIds);
```
(add `formDefinition.AllowedUsers.Select(au => au.UserId).ToList()` to the constructor call)

## 6. `SubmitFormDataCommand.cs`

Same `Include` and `HasAccess` call update as above.

## 7. `FormsController.cs` — one new endpoint

```csharp
public record SetAllowedUsersRequest(IReadOnlyList<Guid> UserIds);

[HttpPut("{id:guid}/allowed-users")]
[Authorize(Roles = "Administrator")]
public async Task<IActionResult> SetAllowedUsers(Guid id, SetAllowedUsersRequest request, CancellationToken cancellationToken)
{
    await _sender.Send(new SetFormAllowedUsersCommand(id, request.UserIds), cancellationToken);
    return NoContent();
}
```
Add `using Platform.Application.Forms.Commands.SetFormAllowedUsers;`.

## Verification

1. `dotnet build`, then `dotnet ef migrations add AddFormAccessUsers` and
   `dotnet ef database update` - new table, same as last round.
2. Confirm nothing broke: existing forms with no restrictions, and the one currently
   restricted by role from last round, both behave exactly as before.
3. Grant a specific non-admin user direct access to a form their role does NOT allow -
   confirm they can now see/fetch/submit it, and confirm a *different* user with the same
   role (but no direct grant) still cannot.
4. Revoke that direct grant - confirm access is gone again for that user, while the
   role-based rule (if any) still applies independently.
5. Confirm a user with neither role access nor a direct grant still gets `403` on
   `GET /api/forms/{id}` and `POST .../submissions`, same as before this round.
