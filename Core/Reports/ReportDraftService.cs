using System.Text.Json;
using System.IO;
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
    private readonly IReportDraftStore _draftStore;
    private readonly IReportService _reportService;
    private readonly IReportHistoryStore _historyStore;
    private readonly string _draftsFolder;

    public ReportDraftService(
        IReportDraftStore draftStore,
        IReportService reportService,
        IReportHistoryStore historyStore)
    {
        _draftStore = draftStore;
        _reportService = reportService;
        _historyStore = historyStore;
        
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

    private string GenerateInitialHtml(ReportConfig config)
    {
        var title = $"{config.ReportType} Report";
        var dateRange = $"{config.DateFrom:yyyy-MM-dd} to {config.DateTo:yyyy-MM-dd}";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>{title}</title>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; background: #f5f5f5; }}
        .container {{ background: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }}
        h1 {{ color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }}
        .metadata {{ background: #ecf0f1; padding: 15px; border-radius: 4px; margin: 20px 0; }}
        .editable {{ min-height: 300px; border: 2px dashed #bdc3c7; padding: 20px; border-radius: 4px; }}
        .editable:hover {{ border-color: #3498db; background: #f8f9fa; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>{title}</h1>
        <div class='metadata'>
            <p><strong>Report Type:</strong> {config.ReportType}</p>
            <p><strong>Date Range:</strong> {dateRange}</p>
            <p><strong>Branch:</strong> {(string.IsNullOrEmpty(config.Branch) ? "All" : config.Branch)}</p>
            <p><strong>Section:</strong> {(string.IsNullOrEmpty(config.Section) ? "All" : config.Section)}</p>
            <p><strong>Status:</strong> Draft - Ready for editing</p>
        </div>
        
        <h2>Report Content</h2>
        <div class='editable' contenteditable='true'>
            <p>This is an editable draft. Click here to modify the content.</p>
            <p>You can add tables, charts, and analysis here.</p>
            <p>Once finalized, export to PDF, Excel, or CSV format.</p>
        </div>
        
        <h2>Additional Sections</h2>
        <div class='editable' contenteditable='true'>
            <h3>Summary</h3>
            <p>Add your summary here...</p>
            
            <h3>Key Findings</h3>
            <ul>
                <li>Finding 1</li>
                <li>Finding 2</li>
            </ul>
            
            <h3>Recommendations</h3>
            <ol>
                <li>Recommendation 1</li>
                <li>Recommendation 2</li>
            </ol>
        </div>
    </div>
</body>
</html>";
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
