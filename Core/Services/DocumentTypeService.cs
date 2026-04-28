using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>
/// Service for managing document types, merging static and database-defined types.
/// </summary>
public interface IDocumentTypeService
{
    /// <summary>
    /// Gets all active document types (static + database-defined, deduplicated and sorted).
    /// </summary>
    string[] GetAllDocumentTypes();

    /// <summary>
    /// Gets active configured document types from Control Panel only.
    /// </summary>
    string[] GetConfiguredDocumentTypes();

    /// <summary>
    /// Gets active document types filtered by section and optional branch.
    /// When section is empty, returns all document types.
    /// When section is provided, returns only types explicitly assigned to that section.
    /// </summary>
    string[] GetDocumentTypesForSection(string? section, string? branch = null);

    /// <summary>
    /// Refreshes the cached document types from the database.
    /// Call this after adding/modifying types in the Control Panel.
    /// </summary>
    void RefreshFromDatabase();
}

public class DocumentTypeService : IDocumentTypeService
{
    private readonly IConfigStore _configStore;
    private string[]? _cachedTypes;
    private string[]? _cachedConfiguredTypes;
    private DateTime? _cachedAt;
    private readonly object _lock = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);

    public DocumentTypeService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public string[] GetAllDocumentTypes()
    {
        lock (_lock)
        {
            if (_cachedTypes == null || _cachedAt == null ||
                DateTime.UtcNow - _cachedAt.Value > _ttl)
            {
                RefreshFromDatabaseInternal();
                _cachedAt = DateTime.UtcNow;
            }
            return _cachedTypes!;
        }
    }

    public string[] GetConfiguredDocumentTypes()
    {
        lock (_lock)
        {
            if (_cachedConfiguredTypes == null || _cachedAt == null ||
                DateTime.UtcNow - _cachedAt.Value > _ttl)
            {
                RefreshFromDatabaseInternal();
                _cachedAt = DateTime.UtcNow;
            }

            return _cachedConfiguredTypes!;
        }
    }

    public string[] GetDocumentTypesForSection(string? section, string? branch = null)
    {
        var normalizedSection = string.IsNullOrWhiteSpace(section) ? null : section.Trim();
        if (string.IsNullOrEmpty(normalizedSection))
            return GetAllDocumentTypes();

        var normalizedBranch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dt in _configStore.GetDocumentTypes(includeInactive: false))
        {
            if (string.IsNullOrWhiteSpace(dt.Name))
                continue;

            var branchMatch = string.IsNullOrEmpty(normalizedBranch) ||
                              string.IsNullOrEmpty(dt.Branch) ||
                              string.Equals(dt.Branch, normalizedBranch, StringComparison.OrdinalIgnoreCase);
            var sectionMatch = !string.IsNullOrEmpty(dt.Section) &&
                               string.Equals(dt.Section, normalizedSection, StringComparison.OrdinalIgnoreCase);
            if (branchMatch && sectionMatch)
                types.Add(dt.Name);
        }

        return types.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void RefreshFromDatabase()
    {
        lock (_lock)
        {
            RefreshFromDatabaseInternal();
        }
    }

    private void RefreshFromDatabaseInternal()
    {
        var dbTypes = _configStore.GetDocumentTypes(includeInactive: false)
            .Select(t => t.Name)
            .ToList();

        var allTypes = new HashSet<string>(DocumentTypeInfo.AllDocTypes);

        foreach (var dbType in dbTypes)
        {
            if (!string.IsNullOrWhiteSpace(dbType))
            {
                allTypes.Add(dbType);
            }
        }

        _cachedTypes = allTypes.OrderBy(t => t).ToArray();
        _cachedConfiguredTypes = dbTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
