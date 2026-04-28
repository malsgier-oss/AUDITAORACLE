using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;
using WorkAudit;
using WorkAudit.Config;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

public partial class AuditorReportsView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<AuditorReportsView>();
    private IReportService? _reportService;
    private IDocumentStore? _documentStore;
    private IConfigStore? _configStore;
    private IReportHistoryStore? _reportHistoryStore;
    private IReportDraftService? _draftService;
    private AppConfiguration? _config;
    private readonly ObservableCollection<ReportHistoryEntry> _reportHistoryAll = new();

    public AuditorReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;

        // Defense in depth: only Auditor and Reviewer should use this view
        _config = ServiceContainer.GetService<AppConfiguration>();
        var roleLevel = Roles.GetRoleLevel(_config?.CurrentUserRole ?? Roles.Viewer);
        if (roleLevel < 2 || roleLevel >= 4)
        {
            MessageText.Text = "This page is for Auditors and Reviewers only.";
            MessageText.Visibility = Visibility.Visible;
            GeneratePdfBtn.IsEnabled = false;
            GenerateExcelBtn.IsEnabled = false;
            GenerateCsvBtn.IsEnabled = false;
            if (DraftsSection != null)
                DraftsSection.Visibility = Visibility.Collapsed;
            return;
        }

        _reportService = ServiceContainer.GetService<IReportService>();
        _documentStore = ServiceContainer.GetService<IDocumentStore>();
        _configStore = ServiceContainer.GetService<IConfigStore>();
        _reportHistoryStore = ServiceContainer.GetOptionalService<IReportHistoryStore>();
        _draftService = ServiceContainer.GetOptionalService<IReportDraftService>();
        if (DraftsSection != null)
            DraftsSection.Visibility = _draftService != null ? Visibility.Visible : Visibility.Collapsed;

        ApplyLocalization();
        LoadPeriodOptions();
        LoadFormatOptions();
        LoadFilterOptions();
        LoadReportHistory();
        LoadDraftsList();
        ApplyPeriodPreset(ReportPeriod.Monthly);
    }

    private void LoadDraftsList()
    {
        if (DraftsList == null) return;
        DraftsList.ItemsSource = null;
        if (OpenDraftInEditorBtn != null)
            OpenDraftInEditorBtn.IsEnabled = false;
        if (_draftService == null || _config == null) return;
        var userId = _config.CurrentUserId ?? "";
        if (string.IsNullOrEmpty(userId)) return;

        try
        {
            var drafts = _draftService.GetUserDrafts(userId);
            var items = drafts
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DraftListItem(d))
                .ToList();
            DraftsList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load report drafts for auditor view");
        }
    }

    private void DraftsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpenDraftInEditorBtn != null)
            OpenDraftInEditorBtn.IsEnabled = DraftsList?.SelectedItem != null;
    }

    private void DraftsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenDraftInEditorBtn_Click(sender, e);
    }

    private void OpenDraftInEditorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DraftsList?.SelectedItem is not DraftListItem row)
            return;
        if (Window.GetWindow(this) is MainWindow mw)
            mw.NavigateToReportEditor(row.Draft.Id);
    }

    private void ApplyLocalization()
    {
        if (_configStore == null) return;
        if (ReportsHeader != null) ReportsHeader.Text = ReportLocalizationService.GetString("MyReports", _configStore);
        if (ReportsSubtitle != null) ReportsSubtitle.Text = ReportLocalizationService.GetString("AuditorReportsSubtitle", _configStore);
        if (PeriodLabel != null) PeriodLabel.Text = ReportLocalizationService.GetString("Period", _configStore);
    }

    private void LoadPeriodOptions()
    {
        PeriodCombo.Items.Clear();
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Weekly", Tag = ReportPeriod.Weekly });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Monthly", Tag = ReportPeriod.Monthly });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "3 months", Tag = ReportPeriod.Quarter });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "6 months", Tag = ReportPeriod.HalfYear });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Yearly", Tag = ReportPeriod.Yearly });
        PeriodCombo.SelectedIndex = 1;
    }

    private void LoadFormatOptions()
    {
        FormatCombo.Items.Clear();
        FormatCombo.Items.Add(new ComboBoxItem { Content = "PDF", Tag = ReportFormat.Pdf });
        FormatCombo.Items.Add(new ComboBoxItem { Content = "Excel", Tag = ReportFormat.Excel });
        FormatCombo.Items.Add(new ComboBoxItem { Content = "CSV", Tag = ReportFormat.Csv });
        FormatCombo.SelectedIndex = 0;
    }

    private void LoadFilterOptions()
    {
        if (_documentStore == null) return;

        BranchCombo.Items.Clear();
        var lockedBranch = Branches.ToConcreteBranchOrDefault(_config?.CurrentUserBranch);
        BranchCombo.Items.Add(lockedBranch);
        BranchCombo.SelectedIndex = 0;
        BranchCombo.IsEnabled = false;

        SectionCombo.Items.Clear();
        SectionCombo.Items.Add("(All)");
        foreach (var s in Enums.SectionValues)
            SectionCombo.Items.Add(s);
        SectionCombo.SelectedIndex = 0;
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodCombo.SelectedItem is ComboBoxItem item && item.Tag is ReportPeriod preset)
            ApplyPeriodPreset(preset);
    }

    private void ApplyPeriodPreset(ReportPeriod preset)
    {
        if (_reportService == null) return;
        var (from, to) = _reportService.GetDateRangeFromPreset(preset);
        DateFromPicker.SelectedDate = from;
        DateToPicker.SelectedDate = to;
    }

    private void DateRange_Changed(object? sender, SelectionChangedEventArgs e) { }

    private ReportConfig GetConfig()
    {
        var config = new ReportConfig
        {
            DateFrom = DateFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1),
            DateTo = DateToPicker.SelectedDate ?? DateTime.Today,
            Preset = (PeriodCombo.SelectedItem as ComboBoxItem)?.Tag as ReportPeriod? ?? ReportPeriod.Monthly,
            Format = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as ReportFormat? ?? ReportFormat.Pdf,
            IncludeCharts = true,
            ReportTemplate = ReportTemplate.Auditor,
            Watermark = ReportWatermark.None
        };

        config.Branch = Branches.ToConcreteBranchOrDefault(_config?.CurrentUserBranch);
        if (SectionCombo.SelectedItem is string section && section != "(All)") config.Section = section;

        config.ReportType = RbDailySummary?.IsChecked == true ? ReportType.DailySummary
            : RbBranchSummary?.IsChecked == true ? ReportType.BranchSummary
            : RbSectionSummary?.IsChecked == true ? ReportType.SectionSummary
            : RbStatusSummary?.IsChecked == true ? ReportType.StatusSummary
            : RbAssignmentSummary?.IsChecked == true ? ReportType.AssignmentSummary
            : RbAuditTrail?.IsChecked == true ? ReportType.AuditTrail
            : ReportType.BranchSummary;

        return config;
    }

    private void LoadReportHistory()
    {
        _reportHistoryAll.Clear();
        if (_reportHistoryStore != null)
        {
            var from = DateTime.UtcNow.AddDays(-30);
            var to = DateTime.UtcNow;
            var history = _reportHistoryStore.List(from, to, 50);
            if (history.Count > 0)
            {
                foreach (var h in history)
                {
                    var ts = DateTime.TryParse(h.GeneratedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : h.GeneratedAt;
                    _reportHistoryAll.Add(new ReportHistoryEntry(ts, h.ReportType, h.FilePath));
                }
                ReportHistoryList.ItemsSource = _reportHistoryAll;
                return;
            }
        }
        var auditLogStore = ServiceContainer.GetOptionalService<IAuditLogStore>();
        if (auditLogStore == null)
        {
            ReportHistoryList.ItemsSource = _reportHistoryAll;
            return;
        }
        var entries = auditLogStore.Query(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, null, Domain.AuditAction.ReportGenerated, Domain.AuditCategory.Report, false, 50, 0);
        foreach (var entry in entries.Where(x => x.Success && !string.IsNullOrEmpty(x.NewValue)))
        {
            var ts = DateTime.TryParse(entry.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : entry.Timestamp;
            var reportType = "Report";
            if (!string.IsNullOrEmpty(entry.Details) && entry.Details.StartsWith("Report type: "))
                reportType = entry.Details.Split(',')[0].Replace("Report type: ", "").Trim();
            _reportHistoryAll.Add(new ReportHistoryEntry(ts, reportType, entry.NewValue!));
        }
        ReportHistoryList.ItemsSource = _reportHistoryAll;
    }

    private void ReportHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportHistoryList.SelectedItem is not ReportHistoryEntry entry) return;
        if (File.Exists(entry.FilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Debug(ex, "Could not open file: {Path}", entry.FilePath); }
        }
    }

    private async void GeneratePdfBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Pdf);
    }

    private async void GenerateExcelBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Excel);
    }

    private async void GenerateCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Csv);
    }

    private async System.Threading.Tasks.Task GenerateReportAsync(ReportFormat format)
    {
        if (_reportService == null)
        {
            ShowMessage("Reports service not available.", isError: true);
            return;
        }

        var config = GetConfig();
        config.Format = format;

        if (format == ReportFormat.Csv && config.ReportType is not (ReportType.DailySummary or ReportType.BranchSummary or ReportType.SectionSummary))
        {
            ShowMessage("CSV export is available for Daily Summary, Branch Summary, and Section Summary only.", isError: true);
            return;
        }

        if (format == ReportFormat.Excel && config.ReportType is not (ReportType.UserActivity or ReportType.AssignmentSummary or ReportType.BranchSummary or ReportType.SectionSummary or ReportType.StatusSummary or ReportType.DocumentTypeSummary))
        {
            ShowMessage("Excel export is available for User Activity, Assignment Summary, Branch, Section, Status, and Document Type reports.", isError: true);
            return;
        }

        if (config.ReportType is ReportType.ExecutiveSummary or ReportType.AuditTrail && format != ReportFormat.Pdf)
        {
            ShowMessage("Executive Summary and Audit Trail are PDF only.", isError: true);
            return;
        }

        SetBusy(true);
        ShowMessage("");

        try
        {
            var path = await _reportService.GenerateAsync(config);

            if (string.IsNullOrEmpty(path))
            {
                ShowMessage("Report generation failed.", isError: true);
                return;
            }

            ShowMessage($"Report generated: {Path.GetFileName(path)}", isError: false);

            var result = MessageBox.Show(
                $"Report generated successfully.\n\n{path}\n\nOpen file?",
                "Report Generated",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex) { Log.Debug(ex, "Could not open file: {Path}", path); }
            }

            _ = Dispatcher.BeginInvoke(new Action(LoadReportHistory), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
        catch (NotImplementedException ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        GeneratePdfBtn.IsEnabled = !busy;
        GenerateExcelBtn.IsEnabled = !busy;
        GenerateCsvBtn.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string message, bool isError = false)
    {
        if (MessageText != null)
        {
            MessageText.Text = message;
            MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            MessageText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
        }
    }

    private sealed record ReportHistoryEntry(string Timestamp, string ReportType, string FilePath)
    {
        public string DisplayText => $"{Timestamp} — {ReportType}";
    }

    private sealed class DraftListItem
    {
        public DraftListItem(ReportDraft d)
        {
            Draft = d;
            var title = !string.IsNullOrWhiteSpace(d.Title)
                ? d.Title!
                : (d.Uuid.Length >= 8 ? d.Uuid[..8] : d.Uuid);
            var when = d.CreatedAt;
            if (DateTime.TryParse(d.CreatedAt, null, DateTimeStyles.RoundtripKind, out var dt))
                when = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            DisplayText = $"{d.ReportType} — {title} — {when}";
        }

        public ReportDraft Draft { get; }
        public string DisplayText { get; }
    }
}
