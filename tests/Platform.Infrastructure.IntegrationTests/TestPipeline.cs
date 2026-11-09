using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>The submission pipeline's collaborators for handlers built by hand in tests.</summary>
public static class TestPipeline
{
    public static IFieldOptionsProviders Providers(params IFieldOptionsProvider[] providers) => new FieldOptionsProviders(providers);

    public static IFormSubmissionHooks Hooks(params IFormSubmissionHook[] hooks) => new FormSubmissionHooks(hooks);

    public static SubmissionPreparer Preparer(IApplicationDbContext db, IDynamicDataRepository data, params IFieldOptionsProvider[] providers) =>
        new(db, data, Providers(providers));

    /// <summary>A caller with the given roles (default: none), for hook contexts.</summary>
    public sealed class Caller(params string[] roles) : ICurrentUserService
    {
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsAuthenticated => true;
    }
}
