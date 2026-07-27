using MediatR;
using Microsoft.Extensions.Logging;

namespace Platform.Application.Common.Behaviors;

public class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger) =>
        _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not Exceptions.ValidationException
            and not Exceptions.NotFoundException and not Exceptions.ForbiddenAccessException)
        {
            _logger.LogError(ex, "Unhandled exception for request {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
