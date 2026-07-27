using Platform.Domain.Common;
using Platform.Domain.Forms.Enums;

namespace Platform.Domain.Forms;

/// <summary>
/// The stable identity of a form. A FormDefinition owns exactly ONE physical dynamic
/// table for its lifetime (TableName, assigned once at first publish and never changed,
/// even if Code is later edited) - this deliberately diverges from a naive
/// "one table per version" scheme. Versions instead snapshot which FieldDefinitions were
/// active at each publish, while schema changes are applied additively to that single
/// table. This keeps querying and reporting simple: no UNION across version tables.
/// </summary>
public class FormDefinition : AuditableEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string ModuleName { get; private set; } = default!;

    /// <summary>Physical table name, e.g. "Data_StockAdjustment". Assigned on first publish.</summary>
    public string? TableName { get; private set; }

    public FormStatus Status { get; private set; } = FormStatus.Draft;
    public int CurrentVersionNumber { get; private set; }

    private readonly List<FormVersion> _versions = new();
    public IReadOnlyCollection<FormVersion> Versions => _versions.AsReadOnly();

    private FormDefinition() { }

    public static FormDefinition Create(string code, string name, string moduleName, string? description)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));

        var definition = new FormDefinition
        {
            Code = NormalizeCode(code),
            Name = name.Trim(),
            ModuleName = moduleName.Trim(),
            Description = description,
            Status = FormStatus.Draft
        };

        definition._versions.Add(FormVersion.CreateInitialDraft(definition.Id));
        return definition;
    }

    public FormVersion? GetPublishedVersion() =>
        _versions.SingleOrDefault(v => v.Status == FormStatus.Published && v.VersionNumber == CurrentVersionNumber);

    public FormVersion GetDraftVersion() =>
        _versions.SingleOrDefault(v => v.Status == FormStatus.Draft)
        ?? throw new InvalidOperationException(
            $"Form '{Code}' has no draft version - call StartNewDraftVersion() first.");

    public FormVersion StartNewDraftVersion()
    {
        if (_versions.Any(v => v.Status == FormStatus.Draft))
            throw new InvalidOperationException($"Form '{Code}' already has an open draft version.");

        var next = FormVersion.CreateDraftFrom(Id, _versions.Where(v => v.Status != FormStatus.Draft)
            .OrderByDescending(v => v.VersionNumber).FirstOrDefault());
        _versions.Add(next);
        return next;
    }

    public void MarkPublished(FormVersion version, string tableName)
    {
        TableName ??= tableName;
        Status = FormStatus.Published;
        CurrentVersionNumber = version.VersionNumber;
    }

    private static string NormalizeCode(string code)
    {
        var slug = new string(code.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');

        if (slug.Length == 0 || !char.IsLetter(slug[0]))
            throw new ArgumentException(
                "Form code must start with a letter after normalization (e.g. 'stock-adjustment').");

        return slug;
    }
}
