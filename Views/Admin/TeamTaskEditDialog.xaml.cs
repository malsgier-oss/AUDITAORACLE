using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class TeamTaskEditDialog : Window
{
    private readonly IUserStore _userStore;
    private readonly List<UserPick> _users = new();

    public TeamTask? ResultTask { get; private set; }
    public DateTime StartDateLocal { get; private set; }
    public DateTime? EndDateLocal { get; private set; }

    public TeamTaskEditDialog(TeamTask? existing, IUserStore userStore)
    {
        InitializeComponent();
        _userStore = userStore;
        foreach (var r in TeamTaskRecurrence.All)
            RecurrenceCombo.Items.Add(r);
        RecurrenceCombo.SelectedItem = TeamTaskRecurrence.Daily;

        foreach (var u in userStore.ListUsers(isActive: true).OrderBy(u => u.DisplayName ?? u.Username))
        {
            var pick = new UserPick(u.Id, u.DisplayName ?? u.Username, u);
            _users.Add(pick);
            UserCombo.Items.Add(pick);
        }

        if (existing != null)
        {
            Title = "Edit team task";
            TitleBox.Text = existing.Title;
            DescriptionBox.Text = existing.Description ?? "";
            RecurrenceCombo.SelectedItem = existing.Recurrence;
            ActiveCheck.IsChecked = existing.IsActive;
            if (DateTime.TryParse(existing.StartDate, out var sd))
                StartDatePicker.SelectedDate = sd.Date;
            if (!string.IsNullOrEmpty(existing.EndDate) && DateTime.TryParse(existing.EndDate, out var ed))
                EndDatePicker.SelectedDate = ed.Date;
            var pick = _users.FirstOrDefault(p => p.Id == existing.AssignedToUserId);
            if (pick != null)
                UserCombo.SelectedItem = pick;
        }
        else
        {
            StartDatePicker.SelectedDate = DateTime.Today;
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show("Please enter a title.", "Team task", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (UserCombo.SelectedItem is not UserPick pick)
        {
            MessageBox.Show("Please select a user.", "Team task", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (RecurrenceCombo.SelectedItem is not string recurrence || !TeamTaskRecurrence.All.Contains(recurrence))
        {
            MessageBox.Show("Please select recurrence.", "Team task", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var start = StartDatePicker.SelectedDate?.Date ?? DateTime.Today;
        DateTime? end = EndDatePicker.SelectedDate?.Date;
        if (end.HasValue && end.Value < start)
        {
            MessageBox.Show("End date cannot be before start date.", "Team task", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultTask = new TeamTask
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            AssignedToUserId = pick.Id,
            AssignedToUsername = pick.User.DisplayName ?? pick.User.Username,
            Recurrence = recurrence,
            IsActive = ActiveCheck.IsChecked == true
        };
        StartDateLocal = start;
        EndDateLocal = end;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed class UserPick
    {
        public UserPick(int id, string displayName, User user)
        {
            Id = id;
            DisplayName = displayName;
            User = user;
        }
        public int Id { get; }
        public string DisplayName { get; }
        public User User { get; }
    }
}
