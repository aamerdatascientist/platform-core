using System.Reflection;
using FluentValidation;
using Platform.Application.Common.Behaviors;
using Platform.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(FormAccessBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<AssignmentNotifier>();

        return services;
    }
}
