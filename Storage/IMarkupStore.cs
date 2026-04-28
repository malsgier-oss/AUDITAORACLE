using WorkAudit.Domain;

namespace WorkAudit.Storage;

public interface IMarkupStore
{
    List<MarkupRegion> GetByDocumentId(int documentId);
    void ReplaceForDocument(int documentId, IReadOnlyList<MarkupRegion> regions, string createdBy);
}
