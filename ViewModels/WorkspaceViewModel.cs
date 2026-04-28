using System.Collections.ObjectModel;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.ViewModels;

/// <summary>
/// ViewModel for the Workspace view. Handles document list loading and filter state.
/// </summary>
public class WorkspaceViewModel : ViewModelBase
{
    private readonly IDocumentStore _store;
    private Document? _selectedDocument;

    public WorkspaceViewModel(IDocumentStore store)
    {
        _store = store;
        CurrentDocuments = new ObservableCollection<Document>();
    }

    public ObservableCollection<Document> CurrentDocuments { get; }

    public Document? SelectedDocument
    {
        get => _selectedDocument;
        set => SetProperty(ref _selectedDocument, value);
    }

    /// <summary>
    /// Loads documents with the given filters and updates CurrentDocuments.
    /// </summary>
    public void LoadDocuments(
        string? branch,
        string? section,
        string? documentType,
        string? dateFrom,
        string? dateTo,
        int limit = 5000)
    {
        var list = _store.ListDocuments(
            branch: branch,
            section: section,
            documentType: documentType,
            dateFrom: dateFrom,
            dateTo: dateTo,
            status: null,
            limit: limit);

        CurrentDocuments.Clear();
        foreach (var doc in list)
            CurrentDocuments.Add(doc);
    }

    /// <summary>
    /// Replaces CurrentDocuments with the given list (e.g. from GetByIds for My Assignments).
    /// </summary>
    public void SetDocuments(IList<Document> documents)
    {
        CurrentDocuments.Clear();
        foreach (var doc in documents)
            CurrentDocuments.Add(doc);
    }
}
