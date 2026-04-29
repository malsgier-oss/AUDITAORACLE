using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace WorkAudit.Config;

/// <summary>
/// User settings loaded/saved from %APPDATA%\WORKAUDIT\user_settings.json
/// </summary>
public static class UserSettings
{
    private static Dictionary<string, object?>? _cache;

    public static Dictionary<string, object?> Load()
    {
        if (_cache != null) return _cache;
        var path = Defaults.GetUserSettingsPath();
        if (!File.Exists(path)) { _cache = new Dictionary<string, object?>(); return _cache; }
        try
        {
            var json = File.ReadAllText(path);
            _cache = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load user settings from {Path}, using defaults", path);
            _cache = new Dictionary<string, object?>();
        }
        return _cache;
    }

    public static bool Save(Dictionary<string, object?> data)
    {
        try
        {
            var path = Defaults.GetUserSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
            _cache = data;
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to save user settings");
            return false;
        }
    }

    public static T? Get<T>(string key, T? defaultValue = default)
    {
        var d = Load();
        if (!d.TryGetValue(key, out var v) || v == null) return defaultValue;
        try
        {
            if (v is Newtonsoft.Json.Linq.JToken jt)
                return jt.ToObject<T>();
            return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture)!;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to convert user setting {Key} to type {Type}, using default", key, typeof(T).Name);
            return defaultValue;
        }
    }

    public static void Set<T>(string key, T value)
    {
        var d = Load();
        d[key] = value;
        Save(d);
    }
}
