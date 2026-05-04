using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Canonical value for <see cref="Document.CreatedBy"/> / <c>documents.created_by</c> so Processing queue filters match inserts.
/// </summary>
public static class DocumentCreatedBy
{
    /// <summary>Uses logged-in username from session config (same as <see cref="WorkAudit.Core.Services.ServiceContainer.SetCurrentUser"/>).</summary>
    public static string? FromAppConfiguration(AppConfiguration appConfig)
    {
        var u = appConfig?.CurrentUserName?.Trim();
        return string.IsNullOrEmpty(u) ? null : u;
    }

    public static string ForUser(User user)
    {
        if (user == null) return "Unknown";
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username.Trim();
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) return user.DisplayName.Trim();
        return "Unknown";
    }
}
