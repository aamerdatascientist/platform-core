using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Api.Security;

internal static class VeronicaCredential
{
    private const string Prefix = "Bearer asas_veronica_";

    public static async Task AuthenticateAsync(MessageReceivedContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return;

        var services = context.HttpContext.RequestServices;
        var configuration = services.GetRequiredService<IConfiguration>();
        var token = authorization["Bearer ".Length..];
        var hash = configuration["Veronica:TokenSha256"];
        if (!string.Equals(configuration["WEBSITE_SITE_NAME"], "asas-api-staging", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(configuration["Veronica:UserId"], out var userId)
            || userId == Guid.Empty
            || !IsAllowedRead(context.Request)
            || !Matches(token, hash))
        {
            context.Fail("Invalid Veronica credential for this request.");
            return;
        }

        var db = services.GetRequiredService<IApplicationDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive && !user.IsDeleted)
            .Select(user => new
            {
                user.Email,
                Roles = db.Roles.Where(role => user.UserRoles.Any(link => link.RoleId == role.Id))
                    .Select(role => role.Name).ToList()
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Fail("Veronica's staging identity is inactive or missing.");
            return;
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Veronica"));
        context.Success();
    }

    private static bool Matches(string token, string? configuredHash)
    {
        if (token.Length != "asas_veronica_".Length + 64 || configuredHash is not { Length: 64 })
            return false;
        try
        {
            var expected = Convert.FromHexString(configuredHash);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAllowedRead(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) || request.QueryString.HasValue)
            return false;

        var path = request.Path.Value ?? "";
        return path is "/api/forms" or "/api/form-assignments/mine"
            or "/api/stage-progress/projects" or "/api/analytics/executive-overview"
            || IsDetail(path, "/api/forms/")
            || IsDetail(path, "/api/stage-progress/projects/");
    }

    private static bool IsDetail(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(path[prefix.Length..], "D", out _);
}
