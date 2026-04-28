using WorkAudit.Domain;

namespace WorkAudit.Core.Services;

/// <summary>
/// In-memory holder for the document currently selected in the app.
/// Used by Chat to include document context when the user asks questions.
/// </summary>
public class CurrentDocumentContextService : ICurrentDocumentContextService
{
    public Document? CurrentDocument { get; set; }

    public void SetCurrentDocument(Document? doc) => CurrentDocument = doc;

    public Document? GetCurrentDocument() => CurrentDocument;
}
