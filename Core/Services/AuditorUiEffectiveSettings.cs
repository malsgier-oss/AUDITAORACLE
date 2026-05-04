using Newtonsoft.Json.Linq;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>Resolves webcam-related settings: per-auditor JSON overrides over <see cref="IConfigStore"/> globals.</summary>
public static class AuditorUiEffectiveSettings
{
    public static bool IsAuditorScoped(string? role) =>
        string.Equals(role, Roles.Auditor, StringComparison.OrdinalIgnoreCase);

    public static bool GetWebcamBool(string? role, JObject? auditorOverrides, IConfigStore config, string key, bool defaultValue)
    {
        if (IsAuditorScoped(role) && auditorOverrides != null && AuditorUiPreferencesJson.TryGetBool(auditorOverrides, key, out var v))
            return v;
        return config.GetSettingBool(key, defaultValue);
    }

    public static int GetWebcamInt(string? role, JObject? auditorOverrides, IConfigStore config, string key, int defaultValue)
    {
        if (IsAuditorScoped(role) && auditorOverrides != null && AuditorUiPreferencesJson.TryGetInt(auditorOverrides, key, out var v))
            return v;
        return config.GetSettingInt(key, defaultValue);
    }
}
