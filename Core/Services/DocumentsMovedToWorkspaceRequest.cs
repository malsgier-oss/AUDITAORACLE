namespace WorkAudit.Core.Services;

/// <summary>
/// Raised when documents are moved to Workspace from Processing. MainWindow updates status and refreshes the Workspace list
/// (without switching away from the current tab).
/// </summary>
public static class DocumentsMovedToWorkspaceRequested
{
    public static event Action<int>? Requested;

    public static void Raise(int count)
    {
        Requested?.Invoke(count);
    }
}
