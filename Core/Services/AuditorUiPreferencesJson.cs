using Newtonsoft.Json.Linq;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>JSON shape in <see cref="IUserAuditorUiPreferencesStore"/> for Auditor-only UI overrides.</summary>
public static class AuditorUiPreferencesJson
{
    public const string KeyboardShortcutsKey = "keyboard_shortcuts";

    public const string EnableAutoCapture = "enable_auto_capture";
    public const string EnableAutoCaptureCooldownTimer = "enable_auto_capture_cooldown_timer";
    public const string AutoCaptureCooldownSeconds = "auto_capture_cooldown_seconds";
    public const string WebcamDefaultScanAreaMode = "webcam_default_scan_area_mode";
    public const string WebcamScanAreaAutoCapture = "webcam_scan_area_auto_capture";

    public static JObject ParseRootOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try
        {
            var o = JObject.Parse(json);
            return o;
        }
        catch
        {
            return new JObject();
        }
    }

    public static bool TryGetBool(JObject root, string key, out bool value)
    {
        value = default;
        if (!root.TryGetValue(key, out var t) || t.Type is JTokenType.Null or JTokenType.Undefined)
            return false;
        try
        {
            value = t.Value<bool>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetInt(JObject root, string key, out int value)
    {
        value = default;
        if (!root.TryGetValue(key, out var t) || t.Type is JTokenType.Null or JTokenType.Undefined)
            return false;
        try
        {
            value = t.Value<int>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Dictionary<string, string>? TryGetShortcutMap(JObject root)
    {
        if (!root.TryGetValue(KeyboardShortcutsKey, out var t) || t is not JObject o)
            return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in o.Properties())
        {
            if (p.Value.Type == JTokenType.String)
                map[p.Name] = p.Value.Value<string>() ?? "";
        }
        return map.Count > 0 ? map : null;
    }

    public static void SetShortcutMap(JObject root, IReadOnlyDictionary<string, string> serializedByCommandId)
    {
        var o = new JObject();
        foreach (var kv in serializedByCommandId)
            o[kv.Key] = kv.Value;
        root[KeyboardShortcutsKey] = o;
    }

    public static void MergeWebcamFields(JObject root,
        bool enableAutoCapture,
        bool showCooldownTimer,
        int cooldownSeconds,
        bool defaultScanAreaMode,
        bool scanAreaAutoCapture)
    {
        root[EnableAutoCapture] = enableAutoCapture;
        root[EnableAutoCaptureCooldownTimer] = showCooldownTimer;
        root[AutoCaptureCooldownSeconds] = cooldownSeconds;
        root[WebcamDefaultScanAreaMode] = defaultScanAreaMode;
        root[WebcamScanAreaAutoCapture] = scanAreaAutoCapture;
    }
}
