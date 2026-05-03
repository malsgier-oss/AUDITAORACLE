using System.IO;
using Newtonsoft.Json;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage;

/// <summary>
/// Saves and recalls common search configurations.
/// </summary>
public class SavedSearch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Branch { get; set; }
    public string? Section { get; set; }
    public string? DocumentType { get; set; }
    public string? Status { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? TextQuery { get; set; }
    public bool UseFullTextSearch { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public interface ISavedSearchService
{
    IReadOnlyList<SavedSearch> List();
    void Save(SavedSearch search);
    void Delete(string id);
    SavedSearch? GetById(string id);
}

public class SavedSearchService : ISavedSearchService
{
    private readonly ILogger _log = LoggingService.ForContext<SavedSearchService>();
    private static string GetStoragePath() =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Defaults.GetUserSettingsPath())!,
            "saved_searches.json");
    private readonly List<SavedSearch> _cache = new();
    private bool _loaded;

    public IReadOnlyList<SavedSearch> List()
    {
        EnsureLoaded();
        return _cache.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public void Save(SavedSearch search)
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

    public SavedSearch? GetById(string id)
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
            var list = JsonConvert.DeserializeObject<List<SavedSearch>>(json);
            if (list != null) _cache.AddRange(list);
        }
        catch (Exception ex) { _log.Warning(ex, "Failed to load saved searches: {Message}", ex.Message); }
    }

    private void Persist()
    {
        try
        {
            var p = GetStoragePath();
            var dir = System.IO.Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }
        catch (Exception ex) { _log.Warning(ex, "Failed to persist saved searches: {Message}", ex.Message); }
    }
}
