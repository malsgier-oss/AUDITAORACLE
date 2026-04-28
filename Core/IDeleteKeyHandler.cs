namespace WorkAudit.Core;

/// <summary>
/// Implemented by views that support "delete selected" when the user presses the Delete key.
/// MainWindow delegates Key.Delete to the active view via this interface.
/// </summary>
public interface IDeleteKeyHandler
{
    /// <summary>
    /// Attempts to handle the Delete key (e.g. delete selected items). Returns true if the key was handled.
    /// </summary>
    bool TryHandleDeleteKey();
}
