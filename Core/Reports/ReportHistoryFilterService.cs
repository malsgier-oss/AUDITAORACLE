using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Service for advanced filtering and searching of report history.
/// </summary>
public interface IReportHistoryFilterService
{
    List<ReportHistory> FilterByTags(List<ReportHistory> reports, string[] tags);
    List<ReportHistory> FilterByDateRange(List<ReportHistory> reports, DateTime? from, DateTime? to);
    List<ReportHistory> FilterByReportType(List<ReportHistory> reports, string[] reportTypes);
    List<ReportHistory> FilterByUser(List<ReportHistory> reports, string userId);
    List<ReportHistory> Search(List<ReportHistory> reports, string searchText);
    List<ReportHistory> ApplyFilters(ReportHistoryFilter filter);
}

/// <summary>
/// Comprehensive filter criteria for report history.
/// </summary>
public class ReportHistoryFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string[]? Tags { get; set; }
    public string[]? ReportTypes { get; set; }
    public string? UserId { get; set; }
    public string? SearchText { get; set; }
    public bool FavoritesOnly { get; set; }
    public int? MinVersion { get; set; }
    public string? Purpose { get; set; }
    public string? SortBy { get; set; } = "date_desc"; // date_desc, date_asc, type, user
    public int Limit { get; set; } = 100;
}

public class ReportHistoryFilterService : IReportHistoryFilterService
{
    private readonly IReportHistoryStore _historyStore;

    public ReportHistoryFilterService(IReportHistoryStore historyStore)
    {
        _historyStore = historyStore;
    }

    public List<ReportHistory> FilterByTags(List<ReportHistory> reports, string[] tags)
    {
        if (tags == null || tags.Length == 0) return reports;

        return reports.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Tags)) return false;
            var reportTags = r.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Trim().ToLowerInvariant());
            return tags.Any(tag => reportTags.Contains(tag.ToLowerInvariant()));
        }).ToList();
    }

    public List<ReportHistory> FilterByDateRange(List<ReportHistory> reports, DateTime? from, DateTime? to)
    {
        return reports.Where(r =>
        {
            if (!DateTime.TryParse(r.GeneratedAt, out var generatedDate)) return true;
            if (from.HasValue && generatedDate < from.Value) return false;
            if (to.HasValue && generatedDate > to.Value) return false;
            return true;
        }).ToList();
    }

    public List<ReportHistory> FilterByReportType(List<ReportHistory> reports, string[] reportTypes)
    {
        if (reportTypes == null || reportTypes.Length == 0) return reports;
        return reports.Where(r => reportTypes.Contains(r.ReportType, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public List<ReportHistory> FilterByUser(List<ReportHistory> reports, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return reports;
        return reports.Where(r => r.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public List<ReportHistory> Search(List<ReportHistory> reports, string searchText)
    {
        if (string.IsNullOrEmpty(searchText)) return reports;

        var searchLower = searchText.ToLowerInvariant();
        return reports.Where(r =>
            r.ReportType.ToLowerInvariant().Contains(searchLower) ||
            r.Username.ToLowerInvariant().Contains(searchLower) ||
            (r.FilePath?.ToLowerInvariant().Contains(searchLower) ?? false) ||
            (r.Tags?.ToLowerInvariant().Contains(searchLower) ?? false) ||
            (r.Purpose?.ToLowerInvariant().Contains(searchLower) ?? false) ||
            (r.Description?.ToLowerInvariant().Contains(searchLower) ?? false)
        ).ToList();
    }

    public List<ReportHistory> ApplyFilters(ReportHistoryFilter filter)
    {
        var from = filter.FromDate ?? DateTime.UtcNow.AddMonths(-3);
        var to = filter.ToDate ?? DateTime.UtcNow;
        
        var reports = _historyStore.List(from, to, filter.Limit * 2);

        if (filter.Tags != null && filter.Tags.Length > 0)
            reports = FilterByTags(reports, filter.Tags);

        if (filter.ReportTypes != null && filter.ReportTypes.Length > 0)
            reports = FilterByReportType(reports, filter.ReportTypes);

        if (!string.IsNullOrEmpty(filter.UserId))
            reports = FilterByUser(reports, filter.UserId);

        if (!string.IsNullOrEmpty(filter.SearchText))
            reports = Search(reports, filter.SearchText);

        if (!string.IsNullOrEmpty(filter.Purpose))
            reports = reports.Where(r => r.Purpose?.Equals(filter.Purpose, StringComparison.OrdinalIgnoreCase) ?? false).ToList();

        if (filter.MinVersion.HasValue)
            reports = reports.Where(r => (r.Version ?? 0) >= filter.MinVersion.Value).ToList();

        reports = filter.SortBy?.ToLowerInvariant() switch
        {
            "date_asc" => reports.OrderBy(r => r.GeneratedAt).ToList(),
            "type" => reports.OrderBy(r => r.ReportType).ThenByDescending(r => r.GeneratedAt).ToList(),
            "user" => reports.OrderBy(r => r.Username).ThenByDescending(r => r.GeneratedAt).ToList(),
            _ => reports.OrderByDescending(r => r.GeneratedAt).ToList()
        };

        return reports.Take(filter.Limit).ToList();
    }
}
