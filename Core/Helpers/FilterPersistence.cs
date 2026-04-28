using WorkAudit.Config;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Helper class for persisting and loading filter values across application sessions.
/// </summary>
public static class FilterPersistence
{
    /// <summary>
    /// Saves a string filter value.
    /// </summary>
    public static void Save(string prefix, string key, string? value)
    {
        try
        {
            UserSettings.Set(prefix + key, value ?? "");
        }
        catch
        {
            // Silently fail on save - not critical
        }
    }

    /// <summary>
    /// Loads a string filter value.
    /// </summary>
    public static string Load(string prefix, string key, string defaultValue = "")
    {
        try
        {
            return UserSettings.Get<string>(prefix + key, defaultValue) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Saves a boolean filter value.
    /// </summary>
    public static void SaveBool(string prefix, string key, bool value)
    {
        try
        {
            UserSettings.Set(prefix + key, value);
        }
        catch
        {
            // Silently fail on save - not critical
        }
    }

    /// <summary>
    /// Loads a boolean filter value.
    /// </summary>
    public static bool LoadBool(string prefix, string key, bool defaultValue = false)
    {
        try
        {
            return UserSettings.Get<bool>(prefix + key, defaultValue);
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Saves a date filter value.
    /// </summary>
    public static void SaveDate(string prefix, string key, DateTime? date)
    {
        try
        {
            UserSettings.Set(prefix + key, date?.ToString("O") ?? "");
        }
        catch
        {
            // Silently fail on save - not critical
        }
    }

    /// <summary>
    /// Loads a date filter value.
    /// </summary>
    public static DateTime? LoadDate(string prefix, string key)
    {
        try
        {
            var value = UserSettings.Get<string>(prefix + key, null);
            if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, out var date))
            {
                return date;
            }
        }
        catch
        {
            // Return null on error
        }
        return null;
    }

    /// <summary>
    /// Clears all filter values for a given prefix.
    /// </summary>
    public static void ClearAll(string prefix, params string[] keys)
    {
        foreach (var key in keys)
        {
            Save(prefix, key, null);
        }
    }
}
