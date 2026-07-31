namespace Platform.Application.Forms;

public static class FormAccessChecker
{
    /// <summary>
    /// The single rule this whole feature rests on: a form with no configured allowed
    /// roles is open to everyone. Restriction is something an admin opts a form into, not
    /// the default - kept in exactly one place so all three call sites (listing, fetching,
    /// submitting) can never disagree about it.
    /// </summary>
    public static bool HasAccess(IReadOnlyCollection<string> allowedRoleNames, IReadOnlyCollection<string> callerRoleNames)
    {
        if (allowedRoleNames.Count == 0) return true;
        return callerRoleNames.Any(r => allowedRoleNames.Contains(r, StringComparer.OrdinalIgnoreCase));
    }
}
