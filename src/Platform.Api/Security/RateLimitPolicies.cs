using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Platform.Api;

/// <summary>
/// Request limits, using ASP.NET Core's built-in rate limiter. Only password-checking
/// endpoints (sign-in, change password) are limited: without a limit anyone could try
/// passwords against an account as fast as the server answers.
///
/// Counted per client IP address. The limit is generous on purpose - a site office's
/// workers may all reach the internet through one shared address - while still cutting a
/// guessing attack to a crawl. Over the limit the answer is 429 with a Retry-After header.
/// The client address comes from X-Forwarded-For (see ForwardedHeaders in Program.cs), since
/// behind Azure App Service's front end every request otherwise looks like it comes from it.
/// </summary>
public static class RateLimitPolicies
{
    public const string SignIn = "sign-in";

    public const int SignInPermitLimit = 20;
    public static readonly TimeSpan SignInWindow = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    """{"title":"Too Many Requests","status":429,"detail":"Too many attempts. Wait a few minutes and try again.","code":"auth.tooManyAttempts"}""",
                    cancellationToken);
            };

            options.AddPolicy(SignIn, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = SignInPermitLimit,
                        Window = SignInWindow,
                        QueueLimit = 0,
                    }));
        });
}
