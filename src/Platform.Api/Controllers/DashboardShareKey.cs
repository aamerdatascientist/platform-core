using System.Security.Cryptography;
using System.Text;

namespace Platform.Api.Controllers;

/// <summary>
/// The secret that makes the public Executive Overview link work without a sign-in.
///
/// It is one value in configuration ("Dashboard:ShareKey" - on Azure App Service the setting
/// is named Dashboard__ShareKey), not a database row: anyone holding a link that carries the
/// current key can view the dashboard, and replacing the value cuts off every link issued
/// before. Leaving it empty switches public viewing off entirely, which is the default.
/// </summary>
public static class DashboardShareKey
{
    public const string ConfigurationKey = "Dashboard:ShareKey";

    /// <summary>The request header the public page sends the key in.</summary>
    public const string HeaderName = "X-Dashboard-Key";

    /// <summary>Shorter than this is treated as "not configured" - a guessable key is worse than none.</summary>
    public const int MinimumLength = 32;

    /// <summary>The configured key, or null when public viewing is switched off.</summary>
    public static string? Read(IConfiguration configuration)
    {
        var key = configuration[ConfigurationKey]?.Trim();
        return string.IsNullOrEmpty(key) || key.Length < MinimumLength ? null : key;
    }

    /// <summary>
    /// True only when a key is configured and <paramref name="presented"/> is exactly it.
    /// Both sides are hashed first so the comparison takes the same time whatever was sent -
    /// no hint leaks about how much of a guess was right, or how long the real key is.
    /// </summary>
    public static bool Matches(IConfiguration configuration, string? presented)
    {
        var expected = Read(configuration);
        if (expected is null || string.IsNullOrEmpty(presented)) return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
    }
}
