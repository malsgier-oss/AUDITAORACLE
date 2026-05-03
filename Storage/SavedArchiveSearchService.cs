using System.IO;
using Newtonsoft.Json;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage;

/// <summary>
/// Saved archive search preset (filters for Archive tab).
/// </summary>
public class SavedArchiveSearch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Branch { get; set; }
    public string? Section { get; set; }
    public string? DocumentType { get; set; }
    public string? LegalHold { get; set; } // "Yes", "No", or null for All
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? ArchivedDateFrom { get; set; }
    public string? ArchivedDateTo { get; set; }
    public int? ExpiringWithinDays { get; set; }
    public string? Tag { get; set; }
    public string? TextQuery { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public interface ISavedArchiveSearchService
{
    IReadOnlyList<SavedArchiveSearch> List();
    void Save(SavedArchiveSearch search);
    void Delete(string id);
    SavedArchiveSearch? GetById(string id);
}

public class SavedArchiveSearchService : ISavedArchiveSearchService
{
    private readonly ILogger _log = LoggingService.ForContext<SavedArchiveSearchService>();
    private static string GetStoragePath() =>
        Path.Combine(
            Path.GetDirectoryName(Defaults.GetUserSettingsPath())!,
            "saved_archive_searches.json");
    private readonly List<SavedArchiveSearch> _cache = new();
    private bool _loaded;

    public IReadOnlyList<SavedArchiveSearch> List()
    {
        EnsureLoaded();
        return _cache.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public void Save(SavedArchiveSearch search)
    {
        EnsureLoaded();
        var existing = _cache.FirstOrDefault(s => s.Id == search.Id);
        if (existing != null)
            _cache.Remove(existing);
        _cache.Add(search);
        Persist();
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        _cache.RemoveAll(s => s.Id == id);
        Persist();
    }

    public SavedArchiveSearch? GetById(string id)
    {
        EnsureLoaded();
        return _cache.FirstOrDefault(s => s.Id == id);
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
            var list = JsonConvert.DeserializeObject<List<SavedArchiveSearch>>(json);
            if (list != null) _cache.AddRange(list);
        }
        catch (Exception ex) { _log.Warning(ex, "Failed to load saved archive searches: {Message}", ex.Message); }
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
        catch (Exception ex) { _log.Warning(ex, "Failed to persist saved archive searches: {Message}", ex.Message); }
    }
}
