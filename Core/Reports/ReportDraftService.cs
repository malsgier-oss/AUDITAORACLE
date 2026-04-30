using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

public interface IReportDraftService
{
    /// <summary>Create a draft from a report configuration (generates initial HTML/preview).</summary>
    ReportDraft CreateDraft(ReportConfig config, string userId, string username);
    
    /// <summary>Update draft metadata (title, notes, tags, finalization status).</summary>
    void UpdateDraft(ReportDraft draft);
    
    /// <summary>Update draft content file (after user edits).</summary>
    void UpdateDraftContent(int draftId, string htmlContent);
    
    /// <summary>Export a draft to final report format.</summary>
    string ExportDraft(int draftId, ReportFormat format);
    
    /// <summary>Delete a draft and its associated files.</summary>
    void DeleteDraft(int draftId);
    
    /// <summary>Get all drafts for a user.</summary>
    List<ReportDraft> GetUserDrafts(string userId);
    
    /// <summary>Get draft by ID.</summary>
    ReportDraft? GetDraft(int draftId);
}

public class ReportDraftService : IReportDraftService
{
    private const int MaxSeedDocuments = 50_000;
    private readonly IReportDraftStore _draftStore;
    private readonly IReportService _reportService;
    private readonly IReportHistoryStore _historyStore;
    private readonly IDocumentStore? _documentStore;
    private readonly string _draftsFolder;

    public ReportDraftService(
        IReportDraftStore draftStore,
        IReportService reportService,
        IReportHistoryStore historyStore,
        IDocumentStore? documentStore = null)
    {
        _draftStore = draftStore;
        _reportService = reportService;
        _historyStore = historyStore;
        _documentStore = documentStore;

        _draftsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WorkAudit",
            "Reports",
            "Drafts");
        
        Directory.CreateDirectory(_draftsFolder);
    }

    public ReportDraft CreateDraft(ReportConfig config, string userId, string username)
    {
        var draft = new ReportDraft
        {
            UserId = userId,
            Username = username,
            ReportType = config.ReportType.ToString(),
            ConfigJson = JsonSerializer.Serialize(config),
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        var draftFileName = $"draft_{draft.Uuid}.html";
        draft.DraftFilePath = Path.Combine(_draftsFolder, draftFileName);

        var htmlContent = GenerateInitialHtml(config);
        File.WriteAllText(draft.DraftFilePath, htmlContent);

        draft.Id = _draftStore.Insert(draft);
        return draft;
    }

    public void UpdateDraft(ReportDraft draft)
    {
        draft.LastModifiedAt = DateTime.UtcNow.ToString("O");
        _draftStore.Update(draft);
    }

    public void UpdateDraftContent(int draftId, string htmlContent)
    {
        var draft = _draftStore.Get(draftId);
        if (draft == null)
            throw new ArgumentException($"Draft {draftId} not found");

        File.WriteAllText(draft.DraftFilePath, htmlContent);
        
        draft.LastModifiedAt = DateTime.UtcNow.ToString("O");
        _draftStore.Update(draft);
    }

    public string ExportDraft(int draftId, ReportFormat format)
    {
        var draft = _draftStore.Get(draftId);
        if (draft == null)
            throw new ArgumentException($"Draft {draftId} not found");

        var config = JsonSerializer.Deserialize<ReportConfig>(draft.ConfigJson);
        if (config == null)
            throw new InvalidOperationException("Failed to deserialize draft configuration");

        config.Format = format;

        var exportedPath = _reportService.Generate(config);

        draft.IsFinalized = true;
        draft.ExportedReportHistoryId = GetReportHistoryUuidByPath(exportedPath);
        UpdateDraft(draft);

        return exportedPath;
    }

    public void DeleteDraft(int draftId)
    {
        var draft = _draftStore.Get(draftId);
        if (draft == null) return;

        if (File.Exists(draft.DraftFilePath))
        {
            try
            {
                File.Delete(draft.DraftFilePath);
            }
            catch
            {
                // Ignore file deletion errors
            }
        }

        _draftStore.Delete(draftId);
    }

    public List<ReportDraft> GetUserDrafts(string userId)
    {
        return _draftStore.GetByUserId(userId);
    }

    public ReportDraft? GetDraft(int draftId)
    {
        return _draftStore.Get(draftId);
    }

    private string GenerateInitialHtml(ReportConfig config) => BuildInitialHtml(config, _documentStore);

    /// <summary>
    /// Builds a draft HTML scaffold seeded with a real snapshot of the documents matching
    /// the supplied <see cref="ReportConfig"/>. Falls back to a metadata-only scaffold when no
    /// document store has been wired in, but never emits placeholder strings like
    /// "Finding 1" / "Recommendation 1": those used to mislead users into thinking the draft
    /// already contained their data. Exposed as <c>public static</c> so tests can verify the
    /// no-placeholder invariant without touching the file system.
    /// </summary>
    public static string BuildInitialHtml(ReportConfig config, IDocumentStore? documentStore)
    {
        var title = $"{config.ReportType} Report";
        var dateRange = $"{config.DateFrom:yyyy-MM-dd} to {config.DateTo:yyyy-MM-dd}";

        var snapshot = TryBuildSnapshot(config, documentStore);

        var sb = new StringBuilder();
        sb.Append(@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>").Append(WebUtility.HtmlEncode(title)).Append(@"</title>
    <style>
        body { font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; background: #f5f5f5; }
        .container { background: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }
        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }
        h2 { color: #34495e; margin-top: 28px; }
        .metadata { background: #ecf0f1; padding: 15px; border-radius: 4px; margin: 20px 0; }
        .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin: 16px 0; }
        .kpi { background: #f8f9fa; border-left: 4px solid #3498db; padding: 12px 16px; border-radius: 4px; }
        .kpi.warn { border-left-color: #e67e22; }
        .kpi.bad { border-left-color: #e74c3c; }
        .kpi.good { border-left-color: #27ae60; }
        .kpi .v { font-size: 24px; font-weight: 700; color: #2c3e50; }
        .kpi .l { font-size: 12px; color: #7f8c8d; text-transform: uppercase; letter-spacing: 0.04em; }
        table { border-collapse: collapse; width: 100%; margin: 8px 0; }
        th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ecf0f1; }
        th { background: #34495e; color: white; }
        .editable { min-height: 120px; border: 2px dashed #bdc3c7; padding: 20px; border-radius: 4px; margin-top: 8px; }
        .editable:hover { border-color: #3498db; background: #f8f9fa; }
        .empty { color: #95a5a6; font-style: italic; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>").Append(WebUtility.HtmlEncode(title)).Append(@"</h1>
        <div class='metadata'>
            <p><strong>Report Type:</strong> ").Append(WebUtility.HtmlEncode(config.ReportType.ToString())).Append(@"</p>
            <p><strong>Date Range:</strong> ").Append(WebUtility.HtmlEncode(dateRange)).Append(@"</p>
            <p><strong>Branch:</strong> ").Append(WebUtility.HtmlEncode(string.IsNullOrEmpty(config.Branch) ? "All" : config.Branch!)).Append(@"</p>
            <p><strong>Section:</strong> ").Append(WebUtility.HtmlEncode(string.IsNullOrEmpty(config.Section) ? "All" : config.Section!)).Append(@"</p>
            <p><strong>Status:</strong> Draft - ").Append(snapshot != null ? "data snapshot loaded below; edit freely" : "metadata only; document store unavailable").Append(@"</p>
        </div>");

        if (snapshot != null)
        {
            sb.Append(@"
        <h2>Snapshot — KPIs at draft creation</h2>
        <div class='kpi-grid'>
            <div class='kpi'><div class='v'>").Append(snapshot.Total.ToString("N0", CultureInfo.InvariantCulture)).Append(@"</div><div class='l'>Total Documents</div></div>
            <div class='kpi good'><div class='v'>").Append(snapshot.Cleared.ToString("N0", CultureInfo.InvariantCulture)).Append(@"</div><div class='l'>Cleared</div></div>
            <div class='kpi ").Append(snapshot.Issues > 0 ? "bad" : "good").Append(@"'><div class='v'>").Append(snapshot.Issues.ToString("N0", CultureInfo.InvariantCulture)).Append(@"</div><div class='l'>Issues</div></div>
            <div class='kpi warn'><div class='v'>").Append(snapshot.InReview.ToString("N0", CultureInfo.InvariantCulture)).Append(@"</div><div class='l'>In Review</div></div>
            <div class='kpi'><div class='v'>").Append(snapshot.ClearingRate.ToString("F1", CultureInfo.InvariantCulture)).Append(@"%</div><div class='l'>Clearing Rate</div></div>
        </div>");

            sb.Append(@"
        <h2>Top branches</h2>");
            AppendBreakdown(sb, snapshot.TopBranches);

            sb.Append(@"
        <h2>Top sections</h2>");
            AppendBreakdown(sb, snapshot.TopSections);

            sb.Append(@"
        <h2>Open issues (most recent ").Append(snapshot.RecentIssues.Count.ToString(CultureInfo.InvariantCulture)).Append(@")</h2>");
            AppendIssueList(sb, snapshot.RecentIssues);
        }
        else
        {
            sb.Append(@"
        <h2>Snapshot</h2>
        <p class='empty'>Document data could not be loaded for the snapshot. The final exported report will still pull live data via the report service.</p>");
        }

        sb.Append(@"
        <h2>Editor notes</h2>
        <div class='editable' contenteditable='true'>
            <p>Add narrative, observations, or attachments here. The snapshot above is a frozen view from the moment the draft was created — when you click Export, the report service regenerates the final document from live data.</p>
        </div>
    </div>
</body>
</html>");

        return sb.ToString();
    }

    private static DraftSnapshot? TryBuildSnapshot(ReportConfig config, IDocumentStore? documentStore)
    {
        if (documentStore == null) return null;
        try
        {
            var fromStr = config.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var toStr = config.DateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
            var docs = documentStore.ListDocuments(
                dateFrom: fromStr,
                dateTo: toStr,
                branch: config.Branch,
                section: config.Section,
                engagement: config.Engagement,
                limit: MaxSeedDocuments,
                newestFirst: true);

            var total = docs.Count;
            var cleared = docs.Count(d => d.Status == Enums.Status.Cleared);
            var issues = docs.Count(d => d.Status == Enums.Status.Issue);
            var inReview = docs.Count(d => d.Status == Enums.Status.Reviewed || d.Status == Enums.Status.ReadyForAudit);
            var active = docs.Count(d => d.Status != Enums.Status.Archived);
            var clearingRate = active > 0 ? (decimal)cleared / active * 100m : 0m;

            return new DraftSnapshot
            {
                Total = total,
                Cleared = cleared,
                Issues = issues,
                InReview = inReview,
                ClearingRate = clearingRate,
                TopBranches = docs
                    .GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch!)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => (g.Key, g.Count()))
                    .ToList(),
                TopSections = docs
                    .GroupBy(d => string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => (g.Key, g.Count()))
                    .ToList(),
                RecentIssues = docs
                    .Where(d => d.Status == Enums.Status.Issue)
                    .OrderByDescending(d => d.Id)
                    .Take(20)
                    .Select(d => (
                        Id: d.Id,
                        FileName: string.IsNullOrEmpty(d.FilePath) ? "(no path)" : Path.GetFileName(d.FilePath),
                        Branch: string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch!,
                        Section: string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section
                    ))
                    .ToList()
            };
        }
        catch
        {
            // Snapshot is best-effort; the final exported report still pulls live data.
            return null;
        }
    }

    private static void AppendBreakdown(StringBuilder sb, List<(string Key, int Count)> rows)
    {
        if (rows.Count == 0)
        {
            sb.Append("<p class='empty'>No data in the selected period.</p>");
            return;
        }
        sb.Append("<table><thead><tr><th>Name</th><th>Documents</th></tr></thead><tbody>");
        foreach (var (key, count) in rows)
        {
            sb.Append("<tr><td>")
              .Append(WebUtility.HtmlEncode(key))
              .Append("</td><td>")
              .Append(count.ToString("N0", CultureInfo.InvariantCulture))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendIssueList(StringBuilder sb, List<(int Id, string FileName, string Branch, string Section)> rows)
    {
        if (rows.Count == 0)
        {
            sb.Append("<p class='empty'>No outstanding issues for the selected scope.</p>");
            return;
        }
        sb.Append("<table><thead><tr><th>ID</th><th>File</th><th>Branch</th><th>Section</th></tr></thead><tbody>");
        foreach (var (id, file, branch, section) in rows)
        {
            sb.Append("<tr><td>")
              .Append(id.ToString(CultureInfo.InvariantCulture))
              .Append("</td><td>")
              .Append(WebUtility.HtmlEncode(file))
              .Append("</td><td>")
              .Append(WebUtility.HtmlEncode(branch))
              .Append("</td><td>")
              .Append(WebUtility.HtmlEncode(section))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private sealed class DraftSnapshot
    {
        public int Total { get; init; }
        public int Cleared { get; init; }
        public int Issues { get; init; }
        public int InReview { get; init; }
        public decimal ClearingRate { get; init; }
        public List<(string Key, int Count)> TopBranches { get; init; } = new();
        public List<(string Key, int Count)> TopSections { get; init; } = new();
        public List<(int Id, string FileName, string Branch, string Section)> RecentIssues { get; init; } = new();
    }

    private string? GetReportHistoryUuidByPath(string path)
    {
        try
        {
            var history = _historyStore.List(limit: 1000)
                .FirstOrDefault(h => h.FilePath == path);
            return history?.Uuid;
        }
        catch
        {
            return null;
        }
    }
}
