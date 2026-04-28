using System.IO;
using Newtonsoft.Json;
using WorkAudit.Config;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// Saved report configuration for one-click report generation.
/// P3: Advanced Features.
/// </summary>
public class SavedReportConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public ReportConfig Config { get; set; } = new();
    public bool IsFavorite { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public interface ISavedReportConfigService
{
    IReadOnlyList<SavedReportConfig> List();
    void Save(SavedReportConfig item);
    void Delete(string id);
    SavedReportConfig? Get(string id);
    void Reorder(IReadOnlyList<string> orderedIds);
}

public class SavedReportConfigService : ISavedReportConfigService
{
    private static string GetStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WORKAUDIT");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "saved_report_configs.json");
    }

    private readonly List<SavedReportConfig> _cache = [];
    private bool _loaded;

    public IReadOnlyList<SavedReportConfig> List()
    {
        EnsureLoaded();
        return _cache.OrderByDescending(s => s.IsFavorite).ThenBy(s => s.DisplayOrder).ThenByDescending(s => s.UpdatedAt ?? s.CreatedAt).ToList();
    }

    public void Save(SavedReportConfig item)
    {
        EnsureLoaded();
        var existing = _cache.FirstOrDefault(s => s.Id == item.Id);
        if (existing != null)
            _cache.Remove(existing);
        item.UpdatedAt = DateTime.UtcNow;
        _cache.Add(item);
        Persist();
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        _cache.RemoveAll(s => s.Id == id);
        Persist();
    }

    public SavedReportConfig? Get(string id)
    {
        EnsureLoaded();
        return _cache.FirstOrDefault(s => s.Id == id);
    }

    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        EnsureLoaded();
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var item = _cache.FirstOrDefault(s => s.Id == orderedIds[i]);
            if (item != null) item.DisplayOrder = i;
        }
        Persist();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var p = GetStoragePath();
            if (!File.Exists(p)) return;
            var json = File.ReadAllText(p);
            var list = JsonConvert.DeserializeObject<List<SavedReportConfig>>(json);
            if (list != null) _cache.AddRange(list);
        }
        catch { /* ignore */ }
    }

    private void Persist()
    {
        try
        {
            var p = GetStoragePath();
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }
        catch { /* ignore */ }
    }
}
