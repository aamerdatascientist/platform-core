using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Identity;

public class PasswordHasher : IPasswordHasher
{
    // Work factor 12 is a reasonable default as of 2026 hardware; revisit periodically -
    // this is a value that should age, not a constant to "set and forget".
    private const int WorkFactor = 12;

    public string Hash(string plainTextPassword) => BCrypt.Net.BCrypt.HashPassword(plainTextPassword, WorkFactor);

    public bool Verify(string plainTextPassword, string hash) => BCrypt.Net.BCrypt.Verify(plainTextPassword, hash);
}
