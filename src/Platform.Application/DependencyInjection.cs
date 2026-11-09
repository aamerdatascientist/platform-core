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
        services.AddScoped<TelegramLinker>();
        services.AddScoped<Forms.SubmissionPreparer>();
        services.AddScoped<Forms.IFieldOptionsProviders, Forms.FieldOptionsProviders>();
        services.AddScoped<Forms.IFormSubmissionHooks, Forms.FormSubmissionHooks>();

        // Stage progress module (2026-11-10): options providers and form hooks.
        services.AddScoped<StageProgress.StageProgressData>();
        services.AddScoped<StageProgress.ProjectSetup>();
        services.AddScoped<Forms.IFieldOptionsProvider, StageProgress.StageOptionsProvider>();
        services.AddScoped<Forms.IFieldOptionsProvider, StageProgress.SubstepOptionsProvider>();
        services.AddScoped<Forms.IFieldOptionsProvider, StageProgress.FloorOptionsProvider>();
        services.AddScoped<Forms.IFieldOptionsProvider, StageProgress.SideOptionsProvider>();
        services.AddScoped<Forms.IFormSubmissionHook, StageProgress.ProjectsHook>();
        services.AddScoped<Forms.IFormSubmissionHook, StageProgress.DailyReportHook>();
        services.AddScoped<Forms.IFormSubmissionHook, StageProgress.PauseLogHook>();
        services.AddScoped<Forms.IFormSubmissionHook, StageProgress.ProjectPlanHook>();
        services.AddScoped<Forms.IFormSubmissionHook, StageProgress.FloorsHook>();

        return services;
    }
}
