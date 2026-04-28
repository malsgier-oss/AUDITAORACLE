using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkAudit.Core.Compliance;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class AuditLogView : UserControl
{
    private readonly IAuditLogStore _auditStore;
    private readonly IPermissionService _permissionService;
    private readonly IAuditExportService _auditExport;
    private List<AuditLogEntry> _entries = new();

    public AuditLogView()
    {
        InitializeComponent();
        _auditStore = ServiceContainer.GetService<IAuditLogStore>();
        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _auditExport = ServiceContainer.GetService<IAuditExportService>();

        CategoryCombo.Items.Add("(All)");
        CategoryCombo.Items.Add(AuditCategory.Authentication);
        CategoryCombo.Items.Add(AuditCategory.Authorization);
        CategoryCombo.Items.Add(AuditCategory.Document);
        CategoryCombo.Items.Add(AuditCategory.User);
        CategoryCombo.Items.Add(AuditCategory.Settings);
        CategoryCombo.Items.Add(AuditCategory.System);
        CategoryCombo.SelectedIndex = 0;

        DateFrom.SelectedDate = System.DateTime.Today.AddDays(-7);
        DateTo.SelectedDate = System.DateTime.Today;

        Loaded += (s, e) => Refresh();
        ExportBtn.IsEnabled = _permissionService.HasPermission(Permissions.AuditLogExport);
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_permissionService.HasPermission(Permissions.AuditLogExport))
        {
            MessageBox.Show("You do not have permission to export the audit log.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var from = DateFrom.SelectedDate ?? DateTime.Today.AddDays(-7);
        var to = DateTo.SelectedDate ?? DateTime.Today;
        var category = CategoryCombo.SelectedItem?.ToString();
        if (category == "(All)") category = null;
        var archivedOnly = ArchiveRelatedOnlyCheck.IsChecked == true;

        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|All files|*.*",
            DefaultExt = ".csv",
            FileName = $"AuditLog_{from:yyyyMMdd}_to_{to:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _auditExport.ExportToFileAsync(dlg.FileName, from, to, null, category, archivedOnly);
            MessageBox.Show($"Exported audit log to {dlg.FileName}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Refresh()
    {
        if (!_permissionService.HasPermission(Permissions.AuditLogView))
        {
            StatusText.Text = "You do not have permission to view the audit log.";
            return;
        }

        var from = DateFrom.SelectedDate;
        var to = DateTo.SelectedDate;
        var category = CategoryCombo.SelectedItem?.ToString();
        if (category == "(All)") category = null;
        var archivedOnly = ArchiveRelatedOnlyCheck.IsChecked == true;

        var fromUtc = from.HasValue ? AuditTimeHelper.ToUtcFromDateUtcPlus2(from.Value) : null;
        var toUtc = to.HasValue ? AuditTimeHelper.ToUtcToDateUtcPlus2(to.Value) : null;
        _entries = _auditStore.Query(fromUtc, toUtc, userId: null, action: null, category, archivedOnly, limit: 500);

        var displayList = _entries.Select(e => new AuditLogDisplayRow
        {
            FormattedTimestamp = AuditTimeHelper.FormatForDisplay(e.Timestamp),
            Username = e.Username,
            Action = e.Action,
            Category = e.Category,
            EntityType = e.EntityType,
            Details = e.Details ?? ""
        }).ToList();
        LogGrid.ItemsSource = null;
        LogGrid.ItemsSource = displayList;
        StatusText.Text = $"{_entries.Count} log entries";
    }

    private sealed class AuditLogDisplayRow
    {
        public string FormattedTimestamp { get; set; } = "";
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
        public string Category { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string Details { get; set; } = "";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();
}
