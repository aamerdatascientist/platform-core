using System.Net;
using System.Text.Json;
using Platform.Application.Common.Exceptions;
using ValidationException = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Api.Middleware;

/// <summary>
/// The single place HTTP status codes get decided from exception types. Handlers throw
/// domain-meaningful exceptions (NotFoundException, ValidationException,
/// ForbiddenAccessException) and never touch HttpContext directly - keeps the
/// Application layer free of any ASP.NET Core dependency.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.BadRequest, "Validation Failed", ex.Message, ex.Errors, ex.Code);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.NotFound, "Not Found", ex.Message);
        }
        catch (ForbiddenAccessException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Forbidden, "Forbidden", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Path}", context.Request.Path);
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError, "Server Error",
                "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context, HttpStatusCode statusCode, string title, string detail, object? errors = null, string? code = null)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { title, status = (int)statusCode, detail, errors, code };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
