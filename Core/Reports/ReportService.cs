using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using Serilog;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Orchestrates report generation. Routes to appropriate generator based on report type.
/// </summary>
public class ReportService : IReportService
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };
    private readonly ILogger _log = LoggingService.ForContext<ReportService>();
    private readonly IDocumentStore _documentStore;
    private readonly IAuditLogStore _auditLogStore;
    private readonly IUserStore _userStore;
    private readonly IConfigStore _configStore;
    private readonly IAuditTrailService _auditTrail;
    private readonly IDocumentAssignmentStore _assignmentStore;
    private readonly IKpiService _kpiService;
    private readonly IRiskScoringService _riskScoringService;
    private readonly IQualityMetricsService? _qualityMetricsService;
    private readonly IReportAnomalyService? _anomalyService;
    private readonly IReportAttestationService _attestationService;
    private readonly IReportHistoryStore _reportHistoryStore;
    private readonly IReportFileOrganizer _fileOrganizer;
    private readonly IReportValidationService _validationService;
    private readonly AppConfiguration _appConfig;

    public ReportService(IDocumentStore documentStore, IAuditLogStore auditLogStore, IUserStore userStore, 
        IConfigStore configStore, IAuditTrailService auditTrail, IDocumentAssignmentStore assignmentStore, 
        IKpiService kpiService, IRiskScoringService riskScoringService, IReportAttestationService attestationService, 
        IReportHistoryStore reportHistoryStore, IReportFileOrganizer fileOrganizer, 
        IReportValidationService validationService, AppConfiguration appConfig, 
        IQualityMetricsService? qualityMetricsService = null, IReportAnomalyService? anomalyService = null)
    {
        _documentStore = documentStore;
        _auditLogStore = auditLogStore;
        _userStore = userStore;
        _configStore = configStore;
        _auditTrail = auditTrail;
        _assignmentStore = assignmentStore;
        _kpiService = kpiService;
        _riskScoringService = riskScoringService;
        _attestationService = attestationService;
        _reportHistoryStore = reportHistoryStore;
        _fileOrganizer = fileOrganizer;
        _validationService = validationService;
        _appConfig = appConfig;
        _qualityMetricsService = qualityMetricsService;
        _anomalyService = anomalyService;
    }

    private int GetRetentionYears() => ReportTemplates.ReportHeaderFooter.GetRetentionYears(_configStore);

    private bool GetTemplateIncludeCharts(ReportConfig config)
    {
        var template = config.ReportTemplate switch
        {
            ReportTemplate.BranchManager => BranchManagerTemplate.GetConfig(),
            ReportTemplate.Auditor => AuditorTemplate.GetConfig(),
            ReportTemplate.Regulatory => RegulatoryTemplate.GetConfig(),
            ReportTemplate.Operations => OperationsTemplate.GetConfig(),
            _ => ExecutiveTemplate.GetConfig()
        };
        return template.IncludeCharts && config.IncludeCharts;
    }

    public (DateTime From, DateTime To) GetDateRangeFromPreset(ReportPeriod preset)
    {
        var to = DateTime.Today;
        var from = preset switch
        {
            ReportPeriod.Weekly => to.AddDays(-7),
            ReportPeriod.Monthly => to.AddMonths(-1),
            ReportPeriod.Quarter => to.AddMonths(-3),
            ReportPeriod.HalfYear => to.AddMonths(-6),
            ReportPeriod.Yearly => to.AddYears(-1),
            _ => to.AddMonths(-1)
        };
        return (from, to);
    }

    public string Generate(ReportConfig config)
    {
        ReportQuestPdf.Configure();
        // Validate configuration
        var validation = _validationService.ValidateConfig(config);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Report configuration is invalid:\n{validation.GetErrorMessage()}");
        }

        // Use organized path if OutputPath is not explicitly set
        if (string.IsNullOrEmpty(config.OutputPath))
        {
            config.OutputPath = _fileOrganizer.GetOrganizedPath(config, config.Format);
            config.OutputPath = _fileOrganizer.EnsureFoldersExist(config.OutputPath);
        }

        string path;
        var reportTypeName = config.ReportType.ToString();

        try
        {
            if (config.Format == ReportFormat.Csv && config.ReportType is ReportType.DailySummary or ReportType.BranchSummary or ReportType.SectionSummary)
            {
                path = ExportToCsv(config);
            }
            else if (config.Format == ReportFormat.Excel)
            {
                path = config.ReportType switch
                {
                    ReportType.UserActivity => UserActivityReport.GenerateExcel(_documentStore, _userStore, _assignmentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.UserFilter, config.OutputPath, config.Engagement),
                    ReportType.AssignmentSummary => AssignmentSummaryReport.GenerateExcel(_assignmentStore, _userStore, config.DateFrom, config.DateTo),
                    ReportType.BranchSummary => ExcelReportHelper.ExportBranchSummary(_documentStore, config.DateFrom, config.DateTo, config.Section, config.Status, config.Branch, config.OutputPath, config.Engagement, config.IncludeCharts),
                    ReportType.SectionSummary => ExcelReportHelper.ExportSectionSummary(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Status, config.Section, config.OutputPath, config.Engagement, config.IncludeCharts),
                    ReportType.StatusSummary => ExcelReportHelper.ExportStatusSummary(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.OutputPath, config.Engagement, config.IncludeCharts),
                    ReportType.DocumentTypeSummary => ExcelReportHelper.ExportDocumentTypeSummary(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.Status, config.OutputPath, config.Engagement, config.IncludeCharts),
                    _ => throw new ArgumentException($"Excel export not supported for {config.ReportType}")
                };
            }
            else if (config.Format == ReportFormat.Pdf && config.ExportPerBranch && config.ReportType is ReportType.BranchSummary or ReportType.SectionSummary or ReportType.Performance)
            {
                path = GeneratePerBranchOrSection(config);
            }
            else
            {
                path = config.ReportType switch
                {
                    ReportType.DailySummary => DailySummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Branch, config.Section, config.Engagement, _configStore, config.Language),
                    ReportType.AuditTrail => ComplianceReports.AuditTrailComplianceReport.GeneratePdf(_auditLogStore, config.DateFrom, config.DateTo, config.OutputPath, 5000, config.Watermark, _configStore, config.Language),
                    ReportType.BranchSummary => BranchSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Section, config.Status, config.Branch, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.SectionSummary => SectionSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Status, config.Section, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.StatusSummary => StatusSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.DocumentTypeSummary => DocumentTypeSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.Status, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.Performance => PerformanceReport.GeneratePdf(_documentStore, _assignmentStore, config.DateFrom, config.DateTo, true, config.Branch, config.Section, config.OutputPath, config.IncludeCharts, GetRetentionYears(), _kpiService, _riskScoringService, _qualityMetricsService, config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.IssuesAndFocus => IssuesAndFocusReport.GeneratePdf(_documentStore, _auditLogStore, _assignmentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.OutputPath, GetRetentionYears(), _anomalyService, config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.UserActivity => UserActivityReport.GeneratePdf(_documentStore, _userStore, _assignmentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.UserFilter, config.OutputPath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language),
                    ReportType.AssignmentSummary => AssignmentSummaryReport.GeneratePdf(_assignmentStore, _userStore, config.DateFrom, config.DateTo, config.OutputPath, GetRetentionYears(), config.Watermark, _configStore, config.Language),
                    ReportType.ExecutiveSummary => ExecutiveSummaryReport.GeneratePdf(_documentStore, _auditLogStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.OutputPath, GetTemplateIncludeCharts(config), GetRetentionYears(), _configStore, config.Watermark, config.Engagement, config.Language, config.IncludeTableOfContents, config.IncludeBranding, config.IncludeDisclaimer),
                    _ => throw new ArgumentOutOfRangeException(nameof(config), config.ReportType, "Unknown report type")
                };
            }

            _log.Information("Report generated: {Type} -> {Path}", reportTypeName, path);

            _ = _auditTrail.LogAsync(
                AuditAction.ReportGenerated,
                AuditCategory.Report,
                "Report",
                Path.GetFileName(path),
                null,
                path,
                $"Report type: {reportTypeName}, Period: {config.DateFrom:yyyy-MM-dd} to {config.DateTo:yyyy-MM-dd}",
                true);

            // Persist report history with full config. A failure here is the exact bug behind the
            // "Recent Reports stays empty after generating" symptom, so log it as an Error (not a
            // Warning) and include the report type/path so support can correlate quickly.
            try
            {
                var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
                var configJson = JsonSerializer.Serialize(config, CompactJsonOptions);
                
                _reportHistoryStore.Insert(new ReportHistory
                {
                    UserId = _appConfig.CurrentUserId ?? "",
                    Username = _appConfig.CurrentUserName ?? "system",
                    ReportType = reportTypeName,
                    FilePath = path,
                    ConfigJson = configJson,
                    AppVersion = appVersion
                });
            }
            catch (Exception ex)
            {
                _log.Error(ex,
                    "Failed to record report history for {Path} ({Type}). The report will not appear in Recent Reports until this is resolved.",
                    path, reportTypeName);
            }

            if (config.Format == ReportFormat.Pdf && File.Exists(path) && config.ReportType != ReportType.ExecutiveSummary)
            {
                try
                {
                    _attestationService.CreateAttestation(
                        reportTypeName,
                        path,
                        config.DateFrom,
                        config.DateTo,
                        config.Branch,
                        config.Section,
                        _appConfig.CurrentUserId,
                        _appConfig.CurrentUserName);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to create report attestation for {Path}", path);
                }
            }

            return path;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate report: {Type}", reportTypeName);
            _ = _auditTrail.LogAsync(AuditAction.ReportGenerated, AuditCategory.Report, "Report", null, null, null, ex.Message, false, ex.Message);
            throw;
        }
    }

    public async Task<string> GenerateAsync(ReportConfig config, IProgress<ReportProgress>? progress = null, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        
        // Report initial progress
        progress?.Report(new ReportProgress
        {
            PercentComplete = 0,
            Stage = "Validating configuration",
            Elapsed = TimeSpan.Zero
        });

        return await Task.Run(() =>
        {
            try
            {
                // Report progress at key stages
                progress?.Report(new ReportProgress
                {
                    PercentComplete = 10,
                    Stage = "Starting report generation",
                    Elapsed = DateTime.UtcNow - startTime
                });

                ct.ThrowIfCancellationRequested();

                var result = Generate(config);

                progress?.Report(new ReportProgress
                {
                    PercentComplete = 100,
                    Stage = "Complete",
                    Elapsed = DateTime.UtcNow - startTime
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                _log.Information("Report generation cancelled by user");
                throw;
            }
        }, ct);
    }

    private string GeneratePerBranchOrSection(ReportConfig config)
    {
        var isByBranch = config.ReportType is ReportType.BranchSummary or ReportType.Performance;
        List<string> items;
        if (isByBranch)
        {
            if (config.ReportType == ReportType.BranchSummary)
            {
                var rows = BranchSummaryReport.GetData(_documentStore, config.DateFrom, config.DateTo, config.Section, config.Status, config.Engagement);
                items = rows.Select(r => r.Branch).ToList();
            }
            else
            {
                var rows = PerformanceReport.GetDataByBranch(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.Engagement);
                items = rows.Select(r => r.Name).ToList();
            }
            if (items.Count == 0) items = _documentStore.GetDistinctBranches();
        }
        else
        {
            var rows = SectionSummaryReport.GetData(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Status, config.Engagement);
            items = rows.Select(r => r.Section).ToList();
            if (items.Count == 0) items = Domain.Enums.SectionValues.ToList();
        }
        if (items.Count == 0) items = isByBranch ? new List<string> { "(No Branch)" } : new List<string> { "(No Section)" };

        var tempDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_PerBranch_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..40]);
        Directory.CreateDirectory(tempDir);
        var generated = 0;
        try
        {
            foreach (var item in items)
            {
                var safeName = string.Join("_", item.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                if (string.IsNullOrEmpty(safeName)) safeName = "Unknown";
                var filePath = Path.Combine(tempDir, $"{config.ReportType}_{safeName}_{config.DateFrom:yyyyMMdd}_{config.DateTo:yyyyMMdd}.pdf");

                if (config.ReportType == ReportType.BranchSummary)
                    _ = BranchSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Section, config.Status, item, filePath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language);
                else if (config.ReportType == ReportType.SectionSummary)
                    _ = SectionSummaryReport.GeneratePdf(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Status, item, filePath, config.IncludeCharts, GetRetentionYears(), config.Watermark, config.Engagement, _configStore, config.Language);
                else if (config.ReportType == ReportType.Performance)
                    _ = PerformanceReport.GeneratePdf(_documentStore, _assignmentStore, config.DateFrom, config.DateTo, true, item, null, filePath, config.IncludeCharts, GetRetentionYears(), _kpiService, _riskScoringService, _qualityMetricsService, config.Watermark, config.Engagement, _configStore, config.Language);

                if (File.Exists(filePath)) generated++;
            }

            if (generated == 0) throw new InvalidOperationException("No reports generated.");

            if (config.ZipPerBranch)
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_{config.ReportType}_PerBranch_{config.DateFrom:yyyyMMdd}_{config.DateTo:yyyyMMdd}.zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tempDir, zipPath);
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
                return zipPath;
            }
            return tempDir;
        }
        catch
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            throw;
        }
    }

    private string ExportToCsv(ReportConfig config)
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_{config.ReportType}_{config.DateFrom:yyyyMMdd}_{config.DateTo:yyyyMMdd}.csv");

        var lines = config.ReportType switch
        {
            ReportType.DailySummary => DailySummaryReport.GetDocumentsPerDay(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Section, config.Engagement)
                .Select(r => $"{r.Date},{r.Count}").Prepend("Date,Documents").ToList(),
            ReportType.BranchSummary => BranchSummaryReport.GetData(_documentStore, config.DateFrom, config.DateTo, config.Section, config.Status, config.Engagement)
                .Select(r => $"{r.Branch},{r.Count}").Prepend("Branch,Documents").ToList(),
            ReportType.SectionSummary => SectionSummaryReport.GetData(_documentStore, config.DateFrom, config.DateTo, config.Branch, config.Status, config.Engagement)
                .Select(r => $"{r.Section},{r.Count}").Prepend("Section,Documents").ToList(),
            _ => new List<string>()
        };

        File.WriteAllLines(csvPath, lines);
        return csvPath;
    }
}
