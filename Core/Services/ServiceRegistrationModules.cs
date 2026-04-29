using Microsoft.Extensions.DependencyInjection;

namespace WorkAudit.Core.Services;

internal static class ServiceRegistrationModules
{
    public static IServiceCollection AddWorkAuditShellModule(this IServiceCollection services)
    {
        services.AddSingleton<IShellPolicyService, ShellPolicyService>();
        services.AddSingleton<ILocalizationApplier, LocalizationApplier>();
        services.AddSingleton<IShellNavigationService, ShellNavigationService>();
        return services;
    }

    public static IServiceCollection AddWorkAuditStartupModule(this IServiceCollection services)
    {
        services.AddSingleton<StartupCoordinator>();
        return services;
    }
}
