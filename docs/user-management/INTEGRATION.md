# User management — integration checklist

New files, self-contained, safe to drop in at matching paths:
- `src/Platform.Application/Identity/Queries/GetUsers/GetUsersQuery.cs`
- `src/Platform.Application/Identity/Queries/GetRoles/GetRolesQuery.cs`
- `src/Platform.Application/Identity/Commands/CreateRole/CreateRoleCommand.cs`
- `src/Platform.Application/Identity/Commands/SetUserRoles/SetUserRolesCommand.cs`
- `src/Platform.Application/Identity/Commands/SetUserActiveStatus/SetUserActiveStatusCommand.cs`
- `src/Platform.Api/Controllers/UsersController.cs`
- `src/Platform.Api/Controllers/RolesController.cs`

No new NuGet packages, no new DI registrations - everything goes through the existing
`IApplicationDbContext` and `ICurrentUserService`.

## 1. Three additions to `src/Platform.Domain/Identity/User.cs`

`Deactivate()` already exists. Add `Activate()` alongside it:
```csharp
public void Activate() => IsActive = true;
```

Modify `AssignRole` to return what it creates, instead of returning void - this is the
actual fix for the tracking gotcha described below, not optional polish:
```csharp
public UserRole? AssignRole(Guid roleId)
{
    if (_userRoles.Any(ur => ur.RoleId == roleId)) return null;
    var userRole = UserRole.Create(Id, roleId);
    _userRoles.Add(userRole);
    return userRole;
}
```

Add a new method, `RemoveRole`:
```csharp
public void RemoveRole(Guid roleId) => _userRoles.RemoveAll(ur => ur.RoleId == roleId);
```

## 2. One addition to `IApplicationDbContext.cs` and `ApplicationDbContext.cs`

`UserRole` (the join entity) has never been exposed as its own `DbSet` - only ever
touched through `User.UserRoles` navigation. `SetUserRolesCommandHandler` needs to
`Add()` to it directly (see the gotcha note below), so add:
```csharp
DbSet<Platform.Domain.Identity.UserRole> UserRoles { get; }
```
and in `ApplicationDbContext.cs`:
```csharp
public DbSet<Platform.Domain.Identity.UserRole> UserRoles => Set<Platform.Domain.Identity.UserRole>();
```

## 3. Lock down public self-registration - a real security change, not routine

In `AuthController.cs`, add `[Authorize(Roles = "Administrator")]` directly above the
`Register` action. Right now anyone, unauthenticated, can create an account by hitting
that endpoint - that's fine for a public product, wrong for an internal company tool
where only admins should be creating accounts. After this change, the frontend's new
"create user" flow (which calls this same endpoint, now with an admin's token attached)
is the only way to create a user.

**Confirm this deliberately**: after the change, try calling `POST /api/auth/register`
with no `Authorization` header at all and confirm it now returns `401`, not `201`.

## Worth understanding, not just applying: the tracking gotcha's new variant

Every prior instance of the EF Core tracking bug (documented in `CLAUDE.md`) involved a
child entity with one client-generated `Guid` key. `UserRole` is different: its key is a
*composite* of two real, already-existing GUIDs (`UserId` + `RoleId`), which arguably
makes it an even easier way to fool EF's new-vs-existing heuristic - both key values
already exist as real rows elsewhere, so a naive check might assume the *combination*
does too. Same fix (explicit `Add()` on the DbSet instead of relying on navigation
fixup from the already-tracked `User`), applied to a shape of the bug that hadn't
actually come up yet. Worth adding as its own line in `CLAUDE.md`'s gotchas section once
this is verified working, since composite-key join entities exist elsewhere in this
codebase too (`RolePermission`, `WorkflowTransitionRole`) and could hit the same thing.

## Verification

1. `dotnet build`. No migration needed - `UserRoles` already exists as a table, this
   only adds a way to query/write it directly through EF.
2. Confirm `POST /api/auth/register` now requires admin auth (see above).
3. **The specific thing worth checking carefully**: assign a *new* role to a user via
   `PUT /api/users/{id}/roles`, then confirm directly in SQL that the new `UserRoles` row
   genuinely exists - not just that the API call returned success. This is exactly the
   kind of silent no-op the gotcha causes if the fix isn't applied correctly.
4. Confirm removing a role via the same endpoint actually deletes the row in SQL too.
5. Confirm a user cannot deactivate their own account (`PUT .../active-status` with
   `isActive: false` on your own user ID should return a clean `400`, not succeed).
6. Confirm a deactivated user can no longer log in (existing `LoginCommandHandler` already
   checks `user.IsActive` - this should already work without any further changes, worth
   confirming rather than assuming).
