namespace Platform.Application.Forms;

public static class FormAccessChecker
{
    /// <summary>
    /// The single rule this whole feature rests on: a form with no configured allowed
    /// roles and no configured allowed users is open to everyone. Restriction is something
    /// an admin opts a form into, not the default - kept in exactly one place so all three
    /// call sites (listing, fetching, submitting) can never disagree about it. A direct
    /// user grant and role-based access are independent: either one alone is enough.
    /// </summary>
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
