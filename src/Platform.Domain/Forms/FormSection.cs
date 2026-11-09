using System.Text.RegularExpressions;
using Platform.Domain.Common;

namespace Platform.Domain.Forms;

/// <summary>
/// A repeatable section (added 2026-11-10): a group of fields the person filling the form can
/// add several times in one submission ("Add another stage"). Each block is stored as one row
/// of a child table, <see cref="ChildTableName"/>, linked to the parent row by "ParentId" and
/// ordered by "BlockIndex"; the parent's own table keeps the fields outside any section.
///
/// Fields belong to a section through <see cref="FieldDefinition.SectionId"/>. The section is
/// shown where its first field would be. Field codes stay unique across the whole version
/// (header and every section), so a visibility condition or a lookup filter can name either a
/// field of the same block or a header field without ambiguity.
/// </summary>
public partial class FormSection : AuditableEntity
{
    public const int MaxBlocksLimit = 50;

    public Guid FormVersionId { get; private set; }

    /// <summary>Latin identifier; names the child table (<c>{parent table}__{PascalCase(Code)}</c>)
    /// and the key the section's blocks travel under in a submission.</summary>
    public string Code { get; private set; } = default!;
    public string TitleAr { get; private set; } = default!;
    public string? TitleEn { get; private set; }
    public int MinBlocks { get; private set; }
    public int MaxBlocks { get; private set; }

    /// <summary>Set once, when the child table is first created (at publish, or on add to a live form). Never changes.</summary>
    public string? ChildTableName { get; private set; }

    private FormSection() { }

    internal static FormSection Create(Guid formVersionId, string code, string titleAr, string? titleEn, int minBlocks, int maxBlocks)
    {
        var normalized = (code ?? string.Empty).Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(normalized))
            throw new ArgumentException("A section code must be Latin letters, digits and underscores, starting with a letter.");
        var section = new FormSection { FormVersionId = formVersionId, Code = normalized };
        section.Rename(titleAr, titleEn);
        section.SetBlockLimits(minBlocks, maxBlocks);
        return section;
    }

    public void Rename(string titleAr, string? titleEn)
    {
        if (string.IsNullOrWhiteSpace(titleAr))
            throw new ArgumentException("A section needs an Arabic title.", nameof(titleAr));
        TitleAr = titleAr.Trim();
        TitleEn = string.IsNullOrWhiteSpace(titleEn) ? null : titleEn.Trim();
    }

    public void SetBlockLimits(int minBlocks, int maxBlocks)
    {
        if (minBlocks < 0 || maxBlocks < 1 || minBlocks > maxBlocks || maxBlocks > MaxBlocksLimit)
            throw new ArgumentException($"Blocks must be between 0 and {MaxBlocksLimit}, with the minimum not above the maximum.");
        MinBlocks = minBlocks;
        MaxBlocks = maxBlocks;
    }

    public void AssignChildTable(string tableName)
    {
        if (ChildTableName is not null && ChildTableName != tableName)
            throw new InvalidOperationException("A section's table name never changes once assigned.");
        ChildTableName = tableName;
    }

    internal static FormSection CopyFor(Guid formVersionId, FormSection source)
    {
        var copy = Create(formVersionId, source.Code, source.TitleAr, source.TitleEn, source.MinBlocks, source.MaxBlocks);
        copy.ChildTableName = source.ChildTableName;
        return copy;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex CodePattern();
}
