using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Security;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

internal static class ServiceRegistrationDiagnostics
{
    private static readonly ILogger Log = LoggingService.ForContext(typeof(ServiceRegistrationDiagnostics));

    public static void LogRegistrationSummary(IServiceCollection services)
    {
        Log.Information("Preparing DI registrations: {Count} service descriptors", services.Count);
    }

    public static void ValidateProvider(IServiceProvider provider)
    {
        ValidateRequired<IConfigStore>(provider);
        ValidateRequired<IDocumentStore>(provider);
        ValidateRequired<ISessionService>(provider);
        ValidateRequired<IPermissionService>(provider);
        ValidateRequired<IReportService>(provider);
    }

    private static void ValidateRequired<T>(IServiceProvider provider) where T : notnull
    {
        _ = provider.GetRequiredService<T>();
        Log.Debug("Validated required service registration: {ServiceType}", typeof(T).Name);
    }
}
