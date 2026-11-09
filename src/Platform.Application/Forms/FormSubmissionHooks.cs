namespace Platform.Application.Forms;

/// <summary>What a hook sees about a write in progress.</summary>
public sealed record SubmissionHookContext(
    PreparedSubmission Submission,
    Guid UserId,
    IReadOnlyCollection<string> UserRoles,
    /// <summary>The record being changed; null when a new one is being created.</summary>
    Guid? RecordId)
{
    public bool IsAdministrator => UserRoles.Contains("Administrator", StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Server-side rules a module attaches to one form, by its code (added 2026-11-10; the stage
/// progress module uses it for projects, the daily report and the pause log). Forms with no
/// hook behave exactly as before.
///
/// Order on create/update: the generic checks pass, then <see cref="LockKey"/> is taken as a
/// Postgres advisory lock for the write's transaction, <see cref="ValidateAsync"/> runs while
/// it is held, the rows are written and committed, and <see cref="AfterSaveAsync"/> runs.
/// Two submissions with the same lock key are therefore checked and written one after the
/// other. AfterSaveAsync work must be safe to repeat (it runs after commit, so a failure there
/// leaves the record saved).
/// </summary>
public interface IFormSubmissionHook
{
    string FormCode { get; }

    string? LockKey(SubmissionHookContext context) => null;

    /// <summary>Throw Common.Exceptions.ValidationException to refuse the write.</summary>
    Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    Task AfterSaveAsync(SubmissionHookContext context, Guid recordId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Throw Common.Exceptions.ValidationException to refuse deleting the record.</summary>
    Task BeforeDeleteAsync(Domain.Forms.FormDefinition form, Guid recordId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IFormSubmissionHooks
{
    IReadOnlyList<IFormSubmissionHook> For(string formCode);
}

public sealed class FormSubmissionHooks(IEnumerable<IFormSubmissionHook> hooks) : IFormSubmissionHooks
{
    private readonly IReadOnlyList<IFormSubmissionHook> _all = hooks.ToList();

    public IReadOnlyList<IFormSubmissionHook> For(string formCode) =>
        _all.Where(h => string.Equals(h.FormCode, formCode, StringComparison.Ordinal)).ToList();
}
