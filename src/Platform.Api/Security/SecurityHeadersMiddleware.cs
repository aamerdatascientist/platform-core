namespace Platform.Api;

/// <summary>
/// Response headers that tell browsers not to sniff, frame, or leak anything from API
/// responses. The API only ever returns JSON or file downloads, never a page, so its content
/// policy can deny everything. Swagger (development only) is left out, since its page needs
/// scripts and styles.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        return _next(context);
    }
}
