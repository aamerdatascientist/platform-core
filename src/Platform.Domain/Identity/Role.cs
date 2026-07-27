using Platform.Domain.Common;

namespace Platform.Domain.Identity;

public class Role : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>
    /// System roles (e.g. "Administrator") cannot be deleted or renamed via the API -
    /// they are seeded once and protected in the application layer.
    /// </summary>
    public bool IsSystemRole { get; private set; }

    private readonly List<RolePermission> _rolePermissions = new();
    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();

    private Role() { }

    public static Role Create(string name, string? description, bool isSystemRole = false) => new()
    {
        Name = name.Trim(),
        Description = description,
        IsSystemRole = isSystemRole
    };

    public void GrantPermission(Guid permissionId)
    {
        if (_rolePermissions.Any(rp => rp.PermissionId == permissionId)) return;
        _rolePermissions.Add(RolePermission.Create(Id, permissionId));
    }

    public void RevokePermission(Guid permissionId) =>
        _rolePermissions.RemoveAll(rp => rp.PermissionId == permissionId);
}

public class Permission : AuditableEntity
{
    /// <summary>
    /// Dot-separated code, e.g. "forms.publish", "forms.data.submit", "users.manage".
    /// Modules register their own permission codes at startup rather than these being hardcoded here.
    /// </summary>
    public string Code { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    private Permission() { }

    public static Permission Create(string code, string description) => new()
    {
        Code = code.Trim().ToLowerInvariant(),
        Description = description
    };
}

public class UserRole
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    private UserRole() { }

    public static UserRole Create(Guid userId, Guid roleId) => new() { UserId = userId, RoleId = roleId };
}

public class RolePermission
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, Guid permissionId) => new() { RoleId = roleId, PermissionId = permissionId };
}
