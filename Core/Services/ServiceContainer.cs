using Microsoft.Extensions.DependencyInjection;
using WorkAudit.Core;
using WorkAudit.Core.Export;
using WorkAudit.Storage;
using WorkAudit.Core.Security;
using WorkAudit.Core.Camera;
using WorkAudit.Core.Import;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.TeamTasks;
using WorkAudit.Core.Compliance;
using WorkAudit.Core.Reports;
using WorkAudit.Core.ImageProcessing;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Core.Update;
using WorkAudit.Domain;
using WorkAudit.ViewModels;

namespace WorkAudit.Core.Services;

/// <summary>
/// Dependency injection container for WorkAudit services.
/// Provides centralized service registration and resolution.
/// </summary>
public static class ServiceContainer
{
    private static IServiceProvider? _provider;
    private static readonly object _lock = new();

    public static IServiceProvider Provider
    {
        get
        {
            if (_provider == null)
                throw new InvalidOperationException("ServiceContainer not initialized. Call Initialize() first.");
            return _provider;
        }
    }

    public static bool IsInitialized => _provider != null;

    public static void Initialize(string oracleConnectionString, string baseDir, string? currentUserId = null)
    {
        lock (_lock)
        {
            if (_provider != null) return;

            var services = new ServiceCollection();
            ConfigureServices(services, oracleConnectionString, baseDir, currentUserId);
            ServiceRegistrationDiagnostics.LogRegistrationSummary(services);
            _provider = services.BuildServiceProvider();
            ServiceRegistrationDiagnostics.ValidateProvider(_provider);

            LoggingService.Info("ServiceContainer initialized with {ServiceCount} services", services.Count);
        }
    }

    private static void ConfigureServices(IServiceCollection services, string oracleConnectionString, string baseDir, string? currentUserId)
    {
        // Configuration
        services.AddSingleton(new AppConfiguration
        {
            OracleConnectionString = oracleConnectionString,
            BaseDirectory = baseDir,
            CurrentUserId = currentUserId
        });

        // Storage
        services.AddSingleton<IDocumentStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new DocumentStore(config.OracleConnectionString, $"local:{config.CurrentUserId ?? "unknown"}");
        });
        services.AddSingleton<IUserStore, UserStore>();
        services.AddSingleton<IAuditLogStore, AuditLogStore>();
        services.AddSingleton<IDocumentAssignmentStore, DocumentAssignmentStore>();
        services.AddSingleton<ITeamTaskStore, TeamTaskStore>();
        services.AddSingleton<IMigrationService>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new MigrationService(config.OracleConnectionString);
        });
        services.AddSingleton<IConfigStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            var secureConfig = sp.GetRequiredService<ISecureConfigService>();
            return new ConfigStore(config.OracleConnectionString, secureConfig);
        });
        services.AddSingleton<IDocumentTypeService>(sp =>
        {
            var configStore = sp.GetRequiredService<IConfigStore>();
            return new DocumentTypeService(configStore);
        });
        services.AddSingleton<INotesStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new NotesStore(config.OracleConnectionString);
        });
        services.AddSingleton<IMarkupStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new MarkupStore(config.OracleConnectionString);
        });

        // Security
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IPermissionService>(sp => new PermissionService(
            sp.GetRequiredService<ISessionService>(),
            sp.GetRequiredService<IConfigStore>(),
            sp.GetRequiredService<IDocumentAssignmentStore>()));
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IPasswordPolicyService, PasswordPolicyService>();
        services.AddSingleton<ISecureDeleteService, SecureDeleteService>();
        services.AddSingleton<IExportEncryptionService, ExportEncryptionService>();
        services.AddSingleton<ISecureConfigService, SecureConfigService>();
        services.AddSingleton<IDatabaseEncryptionService, DatabaseEncryptionService>();
        services.AddSingleton<IHealthCheckService>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            var docStore = sp.GetRequiredService<IDocumentStore>();
            var userStore = sp.GetRequiredService<IUserStore>();
            return new HealthCheckService(config, docStore, userStore);
        });
        services.AddSingleton<IAuditTrailService>(sp =>
        {
            var auditStore = sp.GetRequiredService<IAuditLogStore>();
            var sessionFactory = (Func<ISessionService>)(() => sp.GetRequiredService<ISessionService>());
            return new AuditTrailService(auditStore, sessionFactory);
        });
        services.AddSingleton<IChangeHistoryService>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            var sessionFactory = (Func<ISessionService?>)(() => sp.GetService<ISessionService>());
            return new ChangeHistoryService(config.OracleConnectionString, sessionFactory);
        });

        // Core Services
        services.AddWorkAuditStartupModule();
        services.AddWorkAuditShellModule();
        services.AddSingleton<ICurrentDocumentContextService, CurrentDocumentContextService>();
        services.AddSingleton<IProcessingProgressService, ProcessingProgressService>();
        services.AddSingleton<IProcessingMergeQueueService, ProcessingMergeQueueService>();
        services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IDashboardCacheService, DashboardCacheService>();
        services.AddSingleton<IErrorMessageService, ErrorMessageService>();
        services.AddSingleton<IOracleBackupGateway, OracleDataPumpGateway>();
        services.AddSingleton<IBackupService>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            var encryptionService = sp.GetService<IExportEncryptionService>();
            var oracleGateway = sp.GetService<IOracleBackupGateway>();
            var configStore = sp.GetService<IConfigStore>();
            return new BackupService(config, encryptionService, oracleGateway, configStore);
        });
        services.AddSingleton<IBackupVerificationService, BackupVerificationService>();
        services.AddSingleton<IRecoveryService, RecoveryService>();
        services.AddSingleton<IIntegrityService>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new IntegrityService(config.OracleConnectionString);
        });
        services.AddSingleton<IAuditExportService, AuditExportService>();
        services.AddSingleton<IErasureService, ErasureService>();
        services.AddSingleton<IRetentionService, RetentionService>();
        services.AddSingleton<IScheduledBackupService>(sp =>
            new ScheduledBackupService(sp.GetRequiredService<IBackupService>(), sp.GetRequiredService<IConfigStore>()));
        services.AddSingleton<IReportEmailService, ReportEmailService>();
        services.AddSingleton<IAutoUpdateService>(sp =>
        {
            var configStore = sp.GetRequiredService<IConfigStore>();
            var updateServer = configStore.GetSettingValue("update_server_url", "") ?? "";
            return new AutoUpdateService(updateServer, Config.Defaults.AppVersion);
        });
        services.AddSingleton<IScheduledReportService>(sp =>
        {
            var configStore = sp.GetRequiredService<IConfigStore>();
            var reportService = sp.GetRequiredService<IReportService>();
            var emailService = sp.GetRequiredService<IReportEmailService>();
            return new ScheduledReportService(configStore, reportService, emailService);
        });
        services.AddSingleton<IImmutabilityService, ImmutabilityService>();
        services.AddSingleton<ILegalHoldService, LegalHoldService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IChainOfCustodyService, ChainOfCustodyService>();
        services.AddSingleton<IReportFileOrganizer, ReportFileOrganizer>();
        services.AddSingleton<IReportValidationService, ReportValidationService>();
        services.AddSingleton<IReportHistoryFilterService>(sp =>
        {
            var historyStore = sp.GetRequiredService<IReportHistoryStore>();
            return new ReportHistoryFilterService(historyStore);
        });
        services.AddSingleton<IReportBulkExportService, ReportBulkExportService>();
        services.AddSingleton<IReportComparisonService>(sp =>
        {
            var historyStore = sp.GetRequiredService<IReportHistoryStore>();
            return new ReportComparisonService(historyStore);
        });
        services.AddSingleton<IReportService>(sp =>
        {
            var docStore = sp.GetRequiredService<IDocumentStore>();
            var auditStore = sp.GetRequiredService<IAuditLogStore>();
            var userStore = sp.GetRequiredService<IUserStore>();
            var configStore = sp.GetRequiredService<IConfigStore>();
            var auditTrail = sp.GetRequiredService<IAuditTrailService>();
            var assignmentStore = sp.GetRequiredService<IDocumentAssignmentStore>();
            var kpiService = sp.GetRequiredService<IKpiService>();
            var riskScoringService = sp.GetRequiredService<IRiskScoringService>();
            var attestationService = sp.GetRequiredService<IReportAttestationService>();
            var reportHistoryStore = sp.GetRequiredService<IReportHistoryStore>();
            var fileOrganizer = sp.GetRequiredService<IReportFileOrganizer>();
            var validationService = sp.GetRequiredService<IReportValidationService>();
            var config = sp.GetRequiredService<AppConfiguration>();
            var qualityMetrics = sp.GetService<IQualityMetricsService>();
            var anomalyService = sp.GetService<IReportAnomalyService>();
            return new ReportService(docStore, auditStore, userStore, configStore, auditTrail, assignmentStore, 
                kpiService, riskScoringService, attestationService, reportHistoryStore, fileOrganizer, 
                validationService, config, qualityMetrics, anomalyService);
        });
        services.AddSingleton<IReportBuilderService>(sp =>
        {
            var templateStore = sp.GetRequiredService<IReportTemplateStore>();
            var documentStore = sp.GetRequiredService<IDocumentStore>();
            return new ReportBuilderService(templateStore, documentStore);
        });
        services.AddSingleton<ICustodianService, CustodianService>();
        services.AddSingleton<IDisposalService, DisposalService>();
        services.AddSingleton<IArchiveAnalyticsService, ArchiveAnalyticsService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<INotificationStore, NotificationStore>();
        services.AddSingleton<ISavedReportConfigService, SavedReportConfigService>();
        services.AddSingleton<IKpiService, KpiService>();
        services.AddSingleton<IRiskScoringService, RiskScoringService>();
        services.AddSingleton<IQualityMetricsService, QualityMetricsService>();
        services.AddSingleton<IReportAnomalyService, ReportAnomalyService>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
        services.AddSingleton<IComparativeAnalysisService, ComparativeAnalysisService>();
        services.AddSingleton<IReportAttestationStore, ReportAttestationStore>();
        services.AddSingleton<IReportAttestationService>(sp =>
        {
            var store = sp.GetRequiredService<IReportAttestationStore>();
            var auditTrail = sp.GetRequiredService<IAuditTrailService>();
            return new ReportAttestationService(store, auditTrail);
        });
        services.AddSingleton<IReportDistributionStore, ReportDistributionStore>();
        services.AddSingleton<IReportDistributionService, ReportDistributionService>();
        services.AddSingleton<IReportHistoryStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new ReportHistoryStore(config);
        });
        services.AddSingleton<IReportDraftStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new ReportDraftStore(config.OracleConnectionString);
        });
        services.AddSingleton<IReportDraftService>(sp =>
        {
            var draftStore = sp.GetRequiredService<IReportDraftStore>();
            var reportService = sp.GetRequiredService<IReportService>();
            var historyStore = sp.GetRequiredService<IReportHistoryStore>();
            return new ReportDraftService(draftStore, reportService, historyStore);
        });
        services.AddSingleton<IReportTemplateStore>(sp =>
        {
            var config = sp.GetRequiredService<AppConfiguration>();
            return new ReportTemplateStore(config.OracleConnectionString);
        });
        services.AddSingleton<IAssignmentNotificationService, AssignmentNotificationService>();
        services.AddSingleton<IDocumentAssignmentService>(sp =>
        {
            var store = sp.GetRequiredService<IDocumentAssignmentStore>();
            var userStore = sp.GetRequiredService<IUserStore>();
            var auditTrail = sp.GetRequiredService<IAuditTrailService>();
            var notificationService = sp.GetService<IAssignmentNotificationService>();
            return new DocumentAssignmentService(store, userStore, auditTrail, notificationService);
        });
        services.AddSingleton<ITeamTaskService>(sp =>
        {
            var store = sp.GetRequiredService<ITeamTaskStore>();
            var userStore = sp.GetRequiredService<IUserStore>();
            var permissionService = sp.GetRequiredService<IPermissionService>();
            var auditTrail = sp.GetRequiredService<IAuditTrailService>();
            return new TeamTaskService(store, userStore, permissionService, auditTrail);
        });

        // Search & Export
        services.AddSingleton<ISavedSearchService, SavedSearchService>();
        services.AddSingleton<ISavedArchiveSearchService, SavedArchiveSearchService>();
        services.AddSingleton<ISearchExportService, SearchExportService>();

        // Camera & Import
        services.AddSingleton<ICameraService, CameraService>();
        services.AddSingleton<IFileRenameService, FileRenameService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<IFolderWatchService, FolderWatchService>();
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        services.AddSingleton<TesseractOcrService>(sp =>
        {
            var appConfig = sp.GetRequiredService<AppConfiguration>();
            var configStore = sp.GetRequiredService<IConfigStore>();
            return new TesseractOcrService(appConfig, configStore);
        });
        services.AddSingleton<IOcrService, SelectingOcrService>();
        services.AddSingleton<IWindowsPreviewOcrLayout, TesseractPreviewOcrLayoutService>();

        // ViewModels
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WorkspaceViewModel>();
    }

    public static T GetService<T>() where T : notnull
    {
        return Provider.GetRequiredService<T>();
    }

    public static T? GetOptionalService<T>() where T : class
    {
        return Provider.GetService<T>();
    }

    /// <summary>
    /// Updates the application configuration with the current logged-in user.
    /// Call this after successful login before showing the main window.
    /// </summary>
    public static void SetCurrentUser(User user)
    {
        var config = Provider.GetRequiredService<AppConfiguration>();
        config.CurrentUserId = user.Uuid;
        config.CurrentUserName = user.Username;
        config.CurrentUserRole = user.Role;
        config.CurrentUserBranch = Domain.Branches.ToConcreteBranchOrDefault(user.Branch);
    }

    public static void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _provider = null;
    }
}

/// <summary>
/// Application configuration holder for DI.
/// </summary>
public class AppConfiguration
{
    /// <summary>Oracle ODP.NET connection string (e.g. User Id=...;Password=...;Data Source=...).</summary>
    public string OracleConnectionString { get; set; } = "";
    public string BaseDirectory { get; set; } = "";
    public string? CurrentUserId { get; set; }
    public string? CurrentUserName { get; set; }
    public string? CurrentUserRole { get; set; }
    public string? CurrentUserBranch { get; set; }
}
