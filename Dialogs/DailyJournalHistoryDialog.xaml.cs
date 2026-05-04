using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class DailyJournalHistoryDialog : Window
{
    private readonly INotesStore _notesStore;
    private readonly int _userId;

    public DailyJournalHistoryDialog(int userId)
    {
        InitializeComponent();

        _notesStore = ServiceContainer.GetService<INotesStore>();
        _userId = userId;

        JournalDatePicker.SelectedDate = DateTime.Today;
        LoadJournalEntry(DateTime.Today);
    }

    private void LoadJournalEntry(DateTime date)
    {
        var dateString = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var journalEntry = _notesStore.Search(type: NoteType.Journal, limit: 100)
            .FirstOrDefault(n => n.CreatedByUserId == _userId &&
                                n.Category == dateString);

        if (journalEntry != null)
        {
            EntryDateText.Text = $"Journal Entry for {date:dddd, MMMM dd, yyyy}";

            if (!string.IsNullOrEmpty(journalEntry.UpdatedAt))
            {
                if (DateTime.TryParse(journalEntry.UpdatedAt, out var updated))
                {
                    EntryDateText.Text += $" (Last updated: {updated:g})";
                }
            }

            JournalRtfSerializer.LoadInto(EntryContentRichTextBox, journalEntry.Content);
        }
        else
        {
            EntryDateText.Text = $"No journal entry for {date:dddd, MMMM dd, yyyy}";
            JournalRtfSerializer.LoadInto(EntryContentRichTextBox,
                "You did not write a journal entry on this date.");
        }
    }

    private void JournalDatePicker_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (JournalDatePicker.SelectedDate.HasValue)
        {
            LoadJournalEntry(JournalDatePicker.SelectedDate.Value);
        }
    }

    private void TodayBtn_Click(object sender, RoutedEventArgs e)
    {
        JournalDatePicker.SelectedDate = DateTime.Today;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
