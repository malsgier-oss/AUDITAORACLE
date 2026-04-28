namespace WorkAudit.Domain;

/// <summary>
/// A user-defined quick link for the workspace browser (title + URL).
/// Persisted in user settings.
/// </summary>
public class QuickLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}
