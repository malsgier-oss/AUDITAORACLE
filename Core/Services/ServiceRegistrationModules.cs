using Microsoft.Extensions.DependencyInjection;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

internal static class ServiceRegistrationModules
{
    /// <summary>Registers diagnostics monitors and <see cref="IDiagnosticsService"/> (call after <see cref="IProcessingMergeQueueService"/>).</summary>
    public static IServiceCollection AddWorkAuditDiagnosticsModule(this IServiceCollection services)
    {
        services.AddSingleton<IErrorLogAnalyzer, ErrorLogAnalyzer>();
        services.AddSingleton<IWorkflowMonitor>(sp => new WorkflowMonitor(
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<IDocumentAssignmentStore>(),
            sp.GetRequiredService<IProcessingMergeQueueService>()));
        services.AddSingleton<IServiceStatusMonitor, ServiceStatusMonitor>();
        services.AddSingleton<IDatabaseMonitor, DatabaseMonitor>();
        services.AddSingleton<IConfigurationValidator, ConfigurationValidator>();
        services.AddSingleton<IActivityTracker, ActivityTracker>();
        services.AddSingleton<ISessionMonitor, SessionMonitor>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        return services;
    }

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
