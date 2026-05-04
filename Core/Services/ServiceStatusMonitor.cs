using WorkAudit.Core.Backup;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public interface IServiceStatusMonitor
{
    List<ServiceStatusInfo> CheckAllServices();
}

public sealed class ServiceStatusMonitor : IServiceStatusMonitor
{
    private readonly IFolderWatchService _folderWatch;
    private readonly IScheduledBackupService _scheduledBackup;
    private readonly IScheduledReportService _scheduledReport;
    private readonly IProcessingMergeQueueService _mergeQueue;
    private readonly IConfigStore _configStore;

    public ServiceStatusMonitor(
        IFolderWatchService folderWatch,
        IScheduledBackupService scheduledBackup,
        IScheduledReportService scheduledReport,
        IProcessingMergeQueueService mergeQueue,
        IConfigStore configStore)
    {
        _folderWatch = folderWatch;
        _scheduledBackup = scheduledBackup;
        _scheduledReport = scheduledReport;
        _mergeQueue = mergeQueue;
        _configStore = configStore;
    }

    public List<ServiceStatusInfo> CheckAllServices()
    {
        var list = new List<ServiceStatusInfo>();
        var watchCount = _folderWatch.WatchedPaths.Count;
        list.Add(new ServiceStatusInfo
        {
            ServiceName = "FolderWatchService",
            Status = watchCount > 0 ? "Running" : "Stopped",
            Details = watchCount > 0
                ? $"{watchCount} folder(s) watched: {string.Join("; ", _folderWatch.WatchedPaths.Take(3))}"
                : "No folders configured for auto-import."
        });

        list.Add(new ServiceStatusInfo
        {
            ServiceName = "ScheduledBackupService",
            Status = _scheduledBackup.IsRunning ? "Running" : "Stopped",
            LastActivityUtc = _scheduledBackup.LastBackupAt?.ToUniversalTime(),
            Details = $"Backup enabled={_configStore.GetSettingBool("backup_enabled", true)}; last backup UTC={_scheduledBackup.LastBackupAt:u}"
        });

        var reportsEnabled = _configStore.GetSettingBool("scheduled_reports_enabled", false);
        list.Add(new ServiceStatusInfo
        {
            ServiceName = "ScheduledReportService",
            Status = _scheduledReport.IsRunning ? "Running" : "Stopped",
            LastActivityUtc = _scheduledReport.LastReportAt?.ToUniversalTime(),
            Details = $"Scheduled reports enabled={reportsEnabled}; type={_configStore.GetSettingValue("scheduled_report_type", "")}; last UTC={_scheduledReport.LastReportAt:u}"
        });

        var pending = _mergeQueue.PendingCount;
        var oldest = _mergeQueue.OldestPendingMergeEnqueueUtc;
        var age = oldest.HasValue ? DateTime.UtcNow - oldest.Value : (TimeSpan?)null;
        list.Add(new ServiceStatusInfo
        {
            ServiceName = "ProcessingMergeQueueService",
            Status = pending > 10 || (age.HasValue && age.Value.TotalHours >= 24) ? "Warning" : "Running",
            Details = oldest.HasValue
                ? $"Pending: {pending}; oldest enqueue (UTC): {oldest:O}; age ~{(int)Math.Max(0, age!.Value.TotalMinutes)} min."
                : $"Pending merge job(s): {pending}"
        });

        return list;
    }
}
