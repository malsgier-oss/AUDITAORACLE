namespace WorkAudit.Storage;

/// <summary>Oracle-backed JSON document per user UUID and role (e.g. Auditor webcam + keyboard shortcuts).</summary>
public interface IUserAuditorUiPreferencesStore
{
    /// <summary>Returns null when no row exists.</summary>
    string? TryGetPreferencesJson(string userUuid, string role);

    void UpsertPreferencesJson(string userUuid, string role, string preferencesJson);
}
