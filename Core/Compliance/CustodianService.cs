using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

public interface ICustodianService
{
    List<User> ListCustodians();
    bool AssignCustodian(int documentId, int? custodianId);
    bool AssignCustodianBulk(IEnumerable<int> documentIds, int? custodianId);
}

public class CustodianService : ICustodianService
{
    private readonly IDocumentStore _store;
    private readonly IUserStore _userStore;

    public CustodianService(IDocumentStore store, IUserStore userStore)
    {
        _store = store;
        _userStore = userStore;
    }

    public List<User> ListCustodians()
    {
        return _userStore.ListUsers(isActive: true);
    }

    public bool AssignCustodian(int documentId, int? custodianId)
    {
        return _store.UpdateCustodian(documentId, custodianId);
    }

    public bool AssignCustodianBulk(IEnumerable<int> documentIds, int? custodianId)
    {
        var ok = true;
        foreach (var id in documentIds)
            if (!_store.UpdateCustodian(id, custodianId))
                ok = false;
        return ok;
    }
}
