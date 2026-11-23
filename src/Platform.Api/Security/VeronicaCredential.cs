using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

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

        if (!IsAllowedRead(context.Request) &&
            (!user.Roles.Contains("Administrator") ||
             !await AllowsWorkflowAsync(context.Request, db, configuration, userId)))
        {
            context.Fail("Invalid Veronica credential for this request.");
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

    private static async Task<bool> AllowsWorkflowAsync(
        HttpRequest request, IApplicationDbContext db, IConfiguration configuration, Guid userId)
    {
        if (request.QueryString.HasValue ||
            !Guid.TryParse(configuration["Veronica:WorkflowFormId"], out var fixtureId) || fixtureId == Guid.Empty)
            return false;

        var parts = (request.Path.Value ?? "").Split('/');
        var method = request.Method;
        Guid? attachmentId = null;
        Guid? recordIdToCheck = null;
        switch (parts)
        {
            case ["", "api", "forms", var form, "submissions"] when
                Guid.TryParseExact(form, "D", out var formId) && formId == fixtureId &&
                HttpMethods.IsPost(method) && request.ContentLength is > 0 and <= 4096 &&
                Guid.TryParseExact(request.Headers["Idempotency-Key"].ToString(), "D", out _):
                break;
            case ["", "api", "forms", var form, "submissions", var record] when
                Guid.TryParseExact(form, "D", out var formId) && formId == fixtureId &&
                Guid.TryParseExact(record, "D", out var recordId) && (HttpMethods.IsGet(method) || HttpMethods.IsDelete(method)):
                recordIdToCheck = recordId;
                break;
            case ["", "api", "forms", var form, "records", var record, "attachments"] when
                Guid.TryParseExact(form, "D", out var formId) && formId == fixtureId &&
                Guid.TryParseExact(record, "D", out var recordId) && HttpMethods.IsPost(method) &&
                request.ContentLength is > 0 and <= 4096:
                recordIdToCheck = recordId;
                break;
            case ["", "api", "records", var record, "attachments"] when
                HttpMethods.IsGet(method) && Guid.TryParseExact(record, "D", out var recordId):
                recordIdToCheck = recordId;
                break;
            case ["", "api", "attachments", var file, "download-url"] when
                HttpMethods.IsGet(method) && Guid.TryParseExact(file, "D", out var fileId):
                attachmentId = fileId;
                break;
            case ["", "api", "attachments", var file] when
                HttpMethods.IsDelete(method) && Guid.TryParseExact(file, "D", out var fileId):
                attachmentId = fileId;
                break;
            default:
                return false;
        }

        var cancellation = request.HttpContext.RequestAborted;
        var table = await db.FormDefinitions.AsNoTracking().Where(form =>
            form.Id == fixtureId && !form.IsDeleted && !form.IsSystem && form.Status == FormStatus.Published &&
            form.Code == "veronica-health-probe" &&
            form.Description == "Dedicated fixture for Veronica staging health checks." &&
            db.FormDefinitionUsers.Any(allowed => allowed.FormDefinitionId == fixtureId && allowed.UserId == userId) &&
            !db.FormDefinitionUsers.Any(allowed => allowed.FormDefinitionId == fixtureId && allowed.UserId != userId) &&
            !db.FormDefinitionRoles.Any(allowed => allowed.FormDefinitionId == fixtureId))
            .Select(form => form.TableName).SingleOrDefaultAsync(cancellation);
        if (string.IsNullOrEmpty(table))
            return false;

        if (attachmentId is { } id)
        {
            recordIdToCheck = await db.FileMetadataEntries.AsNoTracking().Where(file =>
                !file.IsDeleted && file.Id == id && file.FormDefinitionId == fixtureId &&
                file.FieldCode == "probe_attachment")
                .Select(file => (Guid?)file.RecordId).SingleOrDefaultAsync(cancellation);
            if (recordIdToCheck is null)
                return false;
        }

        if (recordIdToCheck is not { } recordIdForLookup)
            return true;
        var repository = request.HttpContext.RequestServices.GetRequiredService<IDynamicDataRepository>();
        var row = await repository.GetRecordAsync(table, [], recordIdForLookup, cancellation);
        return row?.CreatedByUserId == userId;
    }
}
