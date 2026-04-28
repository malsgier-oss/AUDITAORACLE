using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

public interface IDisposalService
{
    bool RequestDisposal(IEnumerable<Document> documents, int requestedByUserId);
    bool ApproveDisposal(IEnumerable<Document> documents, int approvedByUserId);
    bool RejectDisposal(IEnumerable<Document> documents, int rejectedByUserId, string? reason = null);
    List<Document> GetPendingDisposals();
}

public class DisposalService : IDisposalService
{
    private readonly IDocumentStore _store;

    public DisposalService(IDocumentStore store)
    {
        _store = store;
    }

    public bool RequestDisposal(IEnumerable<Document> documents, int requestedByUserId)
    {
        var ok = true;
        foreach (var doc in documents)
        {
            if (doc.LegalHold)
                continue;
            if (!_store.UpdateDisposalStatus(doc.Id, "Pending", requestedBy: requestedByUserId))
                ok = false;
        }
        return ok;
    }

    public bool ApproveDisposal(IEnumerable<Document> documents, int approvedByUserId)
    {
        var ok = true;
        foreach (var doc in documents)
        {
            if (doc.DisposalStatus != "Pending")
                continue;
            if (!_store.UpdateDisposalStatus(doc.Id, "Approved", approvedBy: approvedByUserId))
                ok = false;
        }
        return ok;
    }

    public bool RejectDisposal(IEnumerable<Document> documents, int rejectedByUserId, string? reason = null)
    {
        var ok = true;
        foreach (var doc in documents)
        {
            if (doc.DisposalStatus != "Pending")
                continue;
            if (!_store.UpdateDisposalStatus(doc.Id, "Rejected", rejectedBy: rejectedByUserId, rejectionReason: reason))
                ok = false;
        }
        return ok;
    }

    public List<Document> GetPendingDisposals()
    {
        return _store.ListDocuments(status: Enums.Status.Archived, disposalStatus: "Pending", limit: 500);
    }
}
