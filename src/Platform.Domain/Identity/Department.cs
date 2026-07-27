using Platform.Domain.Common;

namespace Platform.Domain.Identity;

public class Department : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public Guid? ParentDepartmentId { get; private set; }

    private Department() { }

    public static Department Create(string name, Guid? parentDepartmentId = null) => new()
    {
        Name = name.Trim(),
        ParentDepartmentId = parentDepartmentId
    };
}

/// <summary>
/// Stores only a hash of the refresh token, never the raw value.
/// One row per issued token so individual sessions can be revoked without
/// invalidating every session for a user.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAtUtc) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = DateTime.UtcNow
    };

    public void Revoke() => RevokedAtUtc = DateTime.UtcNow;
}
