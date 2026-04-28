using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

public class ArchiveAnalytics
{
    public long TotalStorageBytes { get; set; }
    public double TotalStorageGb => TotalStorageBytes / (1024.0 * 1024.0 * 1024.0);
    public decimal CostPerGb { get; set; }
    public decimal EstimatedMonthlyCost => (decimal)TotalStorageGb * CostPerGb;
    public int DocumentCount { get; set; }
    public Dictionary<string, long> StorageByDocumentType { get; set; } = new();
    public Dictionary<string, int> CountByDocumentType { get; set; } = new();
}

public interface IArchiveAnalyticsService
{
    ArchiveAnalytics GetAnalytics();
}

public class ArchiveAnalyticsService : IArchiveAnalyticsService
{
    private readonly IDocumentStore _store;
    private readonly IConfigStore _configStore;

    public ArchiveAnalyticsService(IDocumentStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public ArchiveAnalytics GetAnalytics()
    {
        var analytics = new ArchiveAnalytics();
        analytics.CostPerGb = decimal.TryParse(_configStore.GetSettingValue("archive_cost_per_gb", "0.10"), out var cost) ? cost : 0.10m;

        var docs = _store.ListDocuments(status: Enums.Status.Archived, limit: 100000);
        analytics.DocumentCount = docs.Count;
        long totalBytes = 0;
        var byType = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var byCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in docs)
        {
            var size = d.FileSize ?? 0;
            totalBytes += size;
            var t = d.DocumentType ?? "Unknown";
            byType.TryGetValue(t, out var existing);
            byType[t] = existing + size;
            byCount.TryGetValue(t, out var cnt);
            byCount[t] = cnt + 1;
        }

        analytics.TotalStorageBytes = totalBytes;
        analytics.StorageByDocumentType = byType;
        analytics.CountByDocumentType = byCount;
        return analytics;
    }
}
