using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class AssignDocumentDialog : Window
{
    private readonly IDocumentAssignmentService _assignmentService;
    private readonly IUserStore _userStore;
    private readonly List<Document> _documents;
    private readonly User _assignedBy;

    public AssignDocumentDialog(IEnumerable<Document> documents, User assignedBy)
    {
        InitializeComponent();
        _documents = documents.ToList();
        _assignedBy = assignedBy;
        _assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        _userStore = ServiceContainer.GetService<IUserStore>();

        ApplyLocalization();
        var cfg = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        SelectedCountText.Text = $"{ReportLocalizationService.GetString("SelectedDocuments", cfg)}: {_documents.Count}";
        DocumentList.ItemsSource = _documents.Select(d => Path.GetFileName(d.FilePath) ?? d.Uuid).ToList();

        BranchFilterCombo.Items.Clear();
        BranchFilterCombo.Items.Add("(All)");
        foreach (var b in Branches.All)
            BranchFilterCombo.Items.Add(b);
        BranchFilterCombo.SelectedIndex = 0;

        RefreshUserList();
    }

    private void BranchFilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshUserList();
    }

    private void RefreshUserList()
    {
        var branchFilter = BranchFilterCombo.SelectedItem as string;
        if (branchFilter == "(All)") branchFilter = null;

        var users = _userStore.ListUsers(isActive: true)
            .Where(u => u.Id != _assignedBy.Id)
            .Where(u => Branches.UserMatchesAssigneeBranchFilter(u.Branch, branchFilter))
            .OrderBy(u => u.DisplayName ?? u.Username)
            .Select(u => new UserAssigneeItem(u))
            .ToList();
        UserCombo.ItemsSource = users;
        if (UserCombo.Items.Count > 0)
            UserCombo.SelectedIndex = 0;
    }

    private string GetPriority()
    {
        if (RbUrgent?.IsChecked == true) return AssignmentPriority.Urgent;
        if (RbHigh?.IsChecked == true) return AssignmentPriority.High;
        if (RbLow?.IsChecked == true) return AssignmentPriority.Low;
        return AssignmentPriority.Normal;
    }

    private void AssignBtn_Click(object sender, RoutedEventArgs e)
    {
        if (UserCombo.SelectedItem is not UserAssigneeItem item)
        {
            MessageBox.Show("Please select a user to assign to.", "Assign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var assignTo = item.User;
        if (_documents.Count == 0)
        {
            MessageBox.Show("No documents selected.", "Assign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dueDate = DueDatePicker.SelectedDate;
        var priority = GetPriority();
        var notes = NotesBox?.Text?.Trim();

        try
        {
            _assignmentService.AssignMany(_documents, assignTo, _assignedBy, dueDate, priority, notes);
            MessageBox.Show($"{_documents.Count} document(s) assigned to {assignTo.DisplayName ?? assignTo.Username}.", "Assigned", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to assign: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AssignDocumentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (!ServiceContainer.IsInitialized) return;
        var config = ServiceContainer.GetService<IConfigStore>();
        if (config == null) return;
        // Keep shell layout fixed in LTR; language selection only changes localized strings.
        FlowDirection = ReportLocalizationService.ShellFlowDirection;
        Title = ReportLocalizationService.GetString("AssignDocuments", config);
        if (BranchFilterLabel != null) BranchFilterLabel.Text = ReportLocalizationService.GetString("Branch", config) + ":";
        if (AssignToLabel != null) AssignToLabel.Text = ReportLocalizationService.GetString("AssignTo", config);
        if (DueDateLabel != null) DueDateLabel.Text = ReportLocalizationService.GetString("DueDateOptional", config);
        if (PriorityLabel != null) PriorityLabel.Text = ReportLocalizationService.GetString("Priority", config);
        if (NotesLabel != null) NotesLabel.Text = ReportLocalizationService.GetString("Notes", config);
        if (AssignBtn != null) AssignBtn.Content = ReportLocalizationService.GetString("Assign", config);
        if (CancelBtn != null) CancelBtn.Content = ReportLocalizationService.GetString("Cancel", config);
        if (RbLow != null) RbLow.Content = ReportLocalizationService.GetString("Low", config);
        if (RbNormal != null) RbNormal.Content = ReportLocalizationService.GetString("Normal", config);
        if (RbHigh != null) RbHigh.Content = ReportLocalizationService.GetString("High", config);
        if (RbUrgent != null) RbUrgent.Content = ReportLocalizationService.GetString("Urgent", config);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class UserAssigneeItem
    {
        public User User { get; }
        public string DisplayNameAndBranch { get; }

        public UserAssigneeItem(User user)
        {
            User = user;
            var name = user.DisplayName ?? user.Username;
            var branch = Branches.ScopesToAllBranches(user.Branch)
                ? Branches.AllBranchesLabel
                : string.IsNullOrEmpty(user.Branch) ? "(no branch)" : user.Branch;
            DisplayNameAndBranch = $"{name} ({branch})";
        }
    }
}
