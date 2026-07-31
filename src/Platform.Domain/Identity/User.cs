using Platform.Domain.Common;

namespace Platform.Domain.Identity;

public class User : AuditableEntity
{
    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public Guid? DepartmentId { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<UserRole> _userRoles = new();
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    private User() { } // EF Core

    public static User Create(string email, string displayName, string passwordHash, Guid? departmentId)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new User
        {
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            DepartmentId = departmentId
        };
    }

    public UserRole? AssignRole(Guid roleId)
    {
        if (_userRoles.Any(ur => ur.RoleId == roleId)) return null;
        var userRole = UserRole.Create(Id, roleId);
        _userRoles.Add(userRole);
        return userRole;
    }

    public void RemoveRole(Guid roleId) => _userRoles.RemoveAll(ur => ur.RoleId == roleId);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void ChangePasswordHash(string newHash) => PasswordHash = newHash;
}
