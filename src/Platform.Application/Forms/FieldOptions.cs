using Platform.Domain.Forms;

namespace Platform.Application.Forms;

/// <summary>
/// One option of a choice field as the form shows it. <paramref name="Attributes"/> carry the
/// option's settings that other fields' visibility can depend on (see
/// FieldDefinition.VisibleWhenAttribute) - e.g. a stage's <c>unit_pick</c>.
/// </summary>
public sealed record FieldOption(string Value, string Label, string? LabelEn = null, IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>What a provider is asked with: the field, and the form's current values as text
/// (the header plus, for a field in a repeatable section, that block's values).</summary>
public sealed record FieldOptionsContext(FormDefinition Form, FieldDefinition Field, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// A named, server-side source of options for a choice field (FieldDefinition.OptionsProvider),
/// registered by a module. Offered options are also the only accepted values: the submission
/// checks ask the same provider with the submitted values.
/// </summary>
public interface IFieldOptionsProvider
{
    /// <summary>The name stored on the field, e.g. <c>stage-plan.stages</c>.</summary>
    string Key { get; }

    /// <summary>One line for the form builder's picker.</summary>
    string Description { get; }

    Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken);
}

public interface IFieldOptionsProviders
{
    IFieldOptionsProvider? Find(string? key);
    IReadOnlyList<IFieldOptionsProvider> All { get; }
}

public sealed class FieldOptionsProviders(IEnumerable<IFieldOptionsProvider> providers) : IFieldOptionsProviders
{
    private readonly IReadOnlyList<IFieldOptionsProvider> _all = providers.ToList();
    public IReadOnlyList<IFieldOptionsProvider> All => _all;
    public IFieldOptionsProvider? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : _all.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
}
