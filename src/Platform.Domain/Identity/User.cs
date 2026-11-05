using Platform.Domain.Common;

namespace Platform.Domain.Identity;

public class User : AuditableEntity
{
    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public Guid? DepartmentId { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Mobile number for WhatsApp notifications, international digits without "+"
    /// ("9665XXXXXXXX"); null = this user gets no WhatsApp messages.
    /// </summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>The Telegram chat the bot sends this user's notifications to; null = not linked.</summary>
    public long? TelegramChatId { get; private set; }

    /// <summary>
    /// One-time code in a "Connect Telegram" link (t.me/bot?start=code). The person presses
    /// Start, the bot receives the code and links that chat to this user. Null when no link is open.
    /// </summary>
    public string? TelegramLinkCode { get; private set; }
    public DateTime? TelegramLinkCodeExpiresAtUtc { get; private set; }

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

    /// <summary>Already normalized by the caller (see PhoneNumbers.Normalize); null clears it.</summary>
    public void SetPhoneNumber(string? phoneNumber) => PhoneNumber = phoneNumber;

    /// <summary>Opens a new link (replacing any earlier code); the chat already linked stays until replaced.</summary>
    public void StartTelegramLink(string code, DateTime expiresAtUtc)
    {
        TelegramLinkCode = code;
        TelegramLinkCodeExpiresAtUtc = expiresAtUtc;
    }

    public bool HasOpenTelegramLink(string code, DateTime nowUtc) =>
        TelegramLinkCode is not null && TelegramLinkCode == code && TelegramLinkCodeExpiresAtUtc > nowUtc;

    /// <summary>The person pressed Start with this user's code: messages now go to <paramref name="chatId"/>.</summary>
    public void LinkTelegram(long chatId)
    {
        TelegramChatId = chatId;
        TelegramLinkCode = null;
        TelegramLinkCodeExpiresAtUtc = null;
    }

    public void UnlinkTelegram()
    {
        TelegramChatId = null;
        TelegramLinkCode = null;
        TelegramLinkCodeExpiresAtUtc = null;
    }
}
