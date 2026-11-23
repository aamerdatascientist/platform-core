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

        if (!IsAllowedRead(context.Request))
        {
            var rejection = user.Roles.Contains("Administrator")
                ? await WorkflowRejectionAsync(context.Request, db, configuration, userId)
                : "administrator-required";
            if (rejection is not null)
            {
                context.Response.Headers["X-Veronica-Workflow-Diagnostic"] = rejection;
                context.Fail("Invalid Veronica credential for this request.");
                return;
            }
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

    private static async Task<string?> WorkflowRejectionAsync(
        HttpRequest request, IApplicationDbContext db, IConfiguration configuration, Guid userId)
    {
        if (request.QueryString.HasValue)
            return "route-not-allowed";

        Guid? configuredFixtureId = null;
        var configured = configuration["Veronica:WorkflowFormId"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Guid.TryParse(configured, out var fixtureId) || fixtureId == Guid.Empty)
                return "fixture-setting-invalid";
            configuredFixtureId = fixtureId;
        }

        var parts = (request.Path.Value ?? "").Split('/');
        var method = request.Method;
        Guid? requestedFixtureId = null;
        Guid? attachmentId = null;
        Guid? recordIdToCheck = null;
        switch (parts)
        {
            case ["", "api", "forms", var form, "submissions"] when
                Guid.TryParseExact(form, "D", out var formId) && HttpMethods.IsPost(method):
                if (request.ContentLength is not (> 0 and <= 4096))
                    return "payload-size";
                if (!Guid.TryParseExact(request.Headers["Idempotency-Key"].ToString(), "D", out _))
                    return "idempotency-key";
                requestedFixtureId = formId;
                break;
            case ["", "api", "forms", var form, "submissions", var record] when
                Guid.TryParseExact(form, "D", out var formId) &&
                Guid.TryParseExact(record, "D", out var recordId) && (HttpMethods.IsGet(method) || HttpMethods.IsDelete(method)):
                requestedFixtureId = formId;
                recordIdToCheck = recordId;
                break;
            case ["", "api", "forms", var form, "records", var record, "attachments"] when
                Guid.TryParseExact(form, "D", out var formId) &&
                Guid.TryParseExact(record, "D", out var recordId) && HttpMethods.IsPost(method):
                if (request.ContentLength is not (> 0 and <= 4096))
                    return "payload-size";
                requestedFixtureId = formId;
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
                return "route-not-allowed";
        }

        if (configuredFixtureId is { } configuredId && requestedFixtureId is { } requestedId &&
            configuredId != requestedId)
            return "fixture-id-mismatch";

        var cancellation = request.HttpContext.RequestAborted;
        var expectedId = configuredFixtureId ?? requestedFixtureId;
        var fixtures = await db.FormDefinitions.AsNoTracking().Where(form =>
            (expectedId == null || form.Id == expectedId) &&
            !form.IsDeleted && !form.IsSystem && form.Status == FormStatus.Published &&
            form.Code == "veronica-health-probe" &&
            form.Description == "Dedicated fixture for Veronica staging health checks." &&
            db.FormDefinitionUsers.Any(allowed => allowed.FormDefinitionId == form.Id && allowed.UserId == userId) &&
            !db.FormDefinitionUsers.Any(allowed => allowed.FormDefinitionId == form.Id && allowed.UserId != userId) &&
            !db.FormDefinitionRoles.Any(allowed => allowed.FormDefinitionId == form.Id))
            .Select(form => new { form.Id, form.TableName }).Take(2).ToListAsync(cancellation);
        if (fixtures.Count != 1 || string.IsNullOrEmpty(fixtures[0].TableName))
            return "fixture-not-available";
        var fixture = fixtures[0];

        if (attachmentId is { } id)
        {
            recordIdToCheck = await db.FileMetadataEntries.AsNoTracking().Where(file =>
                !file.IsDeleted && file.Id == id && file.FormDefinitionId == fixture.Id &&
                file.FieldCode == "probe_attachment")
                .Select(file => (Guid?)file.RecordId).SingleOrDefaultAsync(cancellation);
            if (recordIdToCheck is null)
                return "attachment-not-in-fixture";
        }

        if (recordIdToCheck is not { } recordIdForLookup)
            return null;
        var repository = request.HttpContext.RequestServices.GetRequiredService<IDynamicDataRepository>();
        var row = await repository.GetRecordAsync(fixture.TableName!, [], recordIdForLookup, cancellation);
        return row?.CreatedByUserId == userId ? null : "record-not-in-fixture";
    }
}
