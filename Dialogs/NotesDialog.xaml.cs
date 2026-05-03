using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Notes;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class NotesDialog : Window
{
    private readonly int _documentId;
    private readonly string _documentUuid;
    private readonly INotesStore _notesStore;
    private readonly INoteDocumentStatusSync _noteStatusSync;

    public NotesDialog(int documentId, string documentUuid, string documentName, string? initialNoteContent = null)
    {
        InitializeComponent();
        _documentId = documentId;
        _documentUuid = documentUuid;
        _notesStore = ServiceContainer.GetService<INotesStore>();
        _noteStatusSync = ServiceContainer.GetService<INoteDocumentStatusSync>();

        DocumentNameText.Text = documentName;
        TypeCombo.ItemsSource = NoteType.Values;
        TypeCombo.SelectedItem = NoteType.Observation;
        SeverityCombo.ItemsSource = NoteSeverity.Values;
        SeverityCombo.SelectedItem = NoteSeverity.Info;

        LoadNotes();
        if (!string.IsNullOrWhiteSpace(initialNoteContent) && NewNoteContent != null)
            NewNoteContent.Text = initialNoteContent;
    }

    private void LoadNotes()
    {
        var notes = _notesStore.GetByDocumentId(_documentId);
        NotesListControl.ItemsSource = notes;
        NoteCountText.Text = $"{notes.Count} note(s)";
    }

    private void AddNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        var content = NewNoteContent?.Text?.Trim();
        if (string.IsNullOrEmpty(content))
        {
            MessageBox.Show("Please enter note content.", "Add Note", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        var config = ServiceContainer.GetService<AppConfiguration>();

        var note = new Note
        {
            DocumentId = _documentId,
            DocumentUuid = _documentUuid,
            Content = content,
            Type = TypeCombo?.SelectedItem?.ToString() ?? NoteType.Observation,
            Severity = SeverityCombo?.SelectedItem?.ToString() ?? NoteSeverity.Info,
            CreatedBy = config?.CurrentUserName ?? user?.Username ?? "Unknown",
            CreatedByUserId = user?.Id ?? 0,
            Status = NoteStatus.Open
        };
        _notesStore.Add(note);
        _ = _noteStatusSync.OnNoteAddedAsync(note);
        NewNoteContent!.Clear();
        LoadNotes();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void StatusCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox combo && combo.DataContext is Note note)
        {
            var isResolvedIssue =
                string.Equals(note.Type, NoteType.Issue, StringComparison.Ordinal)
                && string.Equals(note.Status, NoteStatus.Resolved, StringComparison.Ordinal);
            combo.IsEnabled = !isResolvedIssue;
            combo.ToolTip = isResolvedIssue ? "Resolved Issue notes cannot change status." : null;

            // Temporarily detach selection changed to prevent circular Reload loop
            combo.SelectionChanged -= StatusCombo_SelectionChanged;

            foreach (ComboBoxItem item in combo.Items)
            {
                if (item?.Tag is string tag && tag == note.Status)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }

            combo.SelectionChanged += StatusCombo_SelectionChanged;
        }
    }

    private void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || sender is not System.Windows.Controls.ComboBox combo) return;
        if (combo.Tag is not int noteId) return;
        if (e.AddedItems[0] is not ComboBoxItem item || item.Tag is not string newStatus) return;

        var note = _notesStore.GetById(noteId);
        if (note == null || note.Status == newStatus) return; // Prevent redundant updates and loop
        var previousStatus = note.Status;

        if (string.Equals(note.Type, NoteType.Issue, StringComparison.Ordinal)
            && string.Equals(previousStatus, NoteStatus.Resolved, StringComparison.Ordinal)
            && !string.Equals(newStatus, NoteStatus.Resolved, StringComparison.Ordinal))
        {
            MessageBox.Show("Resolved Issue notes cannot change status.", "Status Locked", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadNotes();
            return;
        }

        note.Status = newStatus;
        if (newStatus == NoteStatus.Resolved)
        {
            var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
            var config = ServiceContainer.GetService<AppConfiguration>();
            note.ResolvedAt = DateTime.UtcNow.ToString("O");
            note.ResolvedBy = config?.CurrentUserName ?? user?.Username ?? "Unknown";
        }

        if (!_notesStore.Update(note))
        {
            MessageBox.Show("Unable to update note status.", "Update Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadNotes();
            return;
        }
        _ = _noteStatusSync.OnNoteStatusChangedAsync(note, previousStatus);
        LoadNotes();
    }
}
