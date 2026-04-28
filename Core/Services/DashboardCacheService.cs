using WorkAudit.Domain;

namespace WorkAudit.Core.Services;

/// <summary>
/// TTL-based cache for dashboard document lists to avoid repeated DB loads.
/// </summary>
public interface IDashboardCacheService
{
    bool TryGetDocuments(string cacheKey, out List<Document> documents);
    void SetDocuments(string cacheKey, List<Document> documents);
    void Invalidate();
}

public class DashboardCacheService : IDashboardCacheService
{
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(90);

    private record CacheEntry(DateTime CachedAt, List<Document> Documents);

    public bool TryGetDocuments(string cacheKey, out List<Document> documents)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAt < _ttl)
                {
                    documents = entry.Documents;
                    return true;
                }
                _cache.Remove(cacheKey);
            }
            documents = null!;
            return false;
        }
    }

    public void SetDocuments(string cacheKey, List<Document> documents)
    {
        lock (_lock)
        {
            _cache[cacheKey] = new CacheEntry(DateTime.UtcNow, documents);
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}
