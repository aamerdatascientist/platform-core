namespace Platform.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsAuthenticated { get; }
}

public interface IDateTime
{
    DateTime UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string plainTextPassword);
    bool Verify(string plainTextPassword, string hash);
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc);

public interface IJwtTokenService
{
    TokenPair GenerateTokenPair(Guid userId, string email, IEnumerable<string> roles);

    /// <summary>Returns a stable hash of a refresh token for storage/lookup - never the raw value.</summary>
    string HashRefreshToken(string rawRefreshToken);
}
