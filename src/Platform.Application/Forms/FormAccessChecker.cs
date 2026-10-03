namespace Platform.Application.Forms;

public static class FormAccessChecker
{
    private const string AdministratorRoleName = "Administrator";

    /// <summary>
    /// The single rule this whole feature rests on: a form with no configured allowed
    /// roles and no configured allowed users is open to everyone. Restriction is something
    /// an admin opts a form into, not the default - kept in exactly one place so every call
    /// site (the GetFormsList/GetFormDefinition/SubmitFormData handlers, plus every request
    /// routed through FormAccessBehavior) can never disagree about it. A direct user grant
    /// and role-based access are independent: either one alone is enough.
    ///
    /// Administrator always has access, regardless of a form's configured roles/users -
    /// added 2026-10-04 after a real lockout: an admin who restricted a form to a set that
    /// excluded their own account could no longer even reach that form's own Access panel
    /// to undo it, since this check was previously enforced uniformly with no exception.
    /// "Full system access" (the Administrator role's own description in the Roles list)
    /// means exactly that now - this is the one place to look for that bypass before
    /// assuming any per-form restriction logic applies to Administrators too.
    /// </summary>
    public static bool HasAccess(
        IReadOnlyCollection<string> allowedRoleNames,
        IReadOnlyCollection<Guid> allowedUserIds,
        IReadOnlyCollection<string> callerRoleNames,
        Guid? callerUserId)
    {
        if (callerRoleNames.Contains(AdministratorRoleName, StringComparer.OrdinalIgnoreCase)) return true;
        if (allowedRoleNames.Count == 0 && allowedUserIds.Count == 0) return true;
        if (callerUserId.HasValue && allowedUserIds.Contains(callerUserId.Value)) return true;
        return callerRoleNames.Any(r => allowedRoleNames.Contains(r, StringComparer.OrdinalIgnoreCase));
    }
}
