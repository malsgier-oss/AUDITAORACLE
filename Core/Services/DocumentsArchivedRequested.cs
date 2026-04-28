namespace WorkAudit.Core.Services;

/// <summary>
/// Raised when documents are archived from Processing. MainWindow switches to Archive view and refreshes.
/// </summary>
public static class DocumentsArchivedRequested
{
    public static event Action<int>? Requested;

    public static void Raise(int count)
    {
        Requested?.Invoke(count);
    }
}
