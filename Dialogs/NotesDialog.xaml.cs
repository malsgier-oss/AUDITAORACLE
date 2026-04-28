using System.Windows;
using System.Windows.Controls;
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

    public NotesDialog(int documentId, string documentUuid, string documentName, string? initialNoteContent = null)
    {
        InitializeComponent();
        _documentId = documentId;
        _documentUuid = documentUuid;
        _notesStore = ServiceContainer.GetService<INotesStore>();

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

        var note = _notesStore.Get(noteId);
        if (note == null || note.Status == newStatus) return; // Prevent redundant updates and loop

        note.Status = newStatus;
        if (newStatus == NoteStatus.Resolved)
        {
            var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
            var config = ServiceContainer.GetService<AppConfiguration>();
            note.ResolvedAt = DateTime.UtcNow.ToString("O");
            note.ResolvedBy = config?.CurrentUserName ?? user?.Username ?? "Unknown";
        }

        _notesStore.Update(note);
        LoadNotes();
    }
}
