using WorkAudit.Domain;

namespace WorkAudit.Core.Services;

/// <summary>
/// Holds the currently selected document for Chat context.
/// Views (Workspace, Search, Processing) set this when the user selects a document.
/// Chat uses it to include document content when asking questions.
/// </summary>
public interface ICurrentDocumentContextService
{
    /// <summary>Currently selected document, or null if none.</summary>
    Document? CurrentDocument { get; set; }

    /// <summary>Set the current document (called by views when selection changes).</summary>
    void SetCurrentDocument(Document? doc);

    /// <summary>Get the current document.</summary>
    Document? GetCurrentDocument();
}
