using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Serilog;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

internal sealed record DrillDownItem(string EntityType, string Branch, string Display);

public partial class ExecutiveDashboardView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<ExecutiveDashboardView>();
    private DateTime _dateFrom;
    private DateTime _dateTo;

    public ExecutiveDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void SetPeriod(DateTime from, DateTime to)
    {
        _dateFrom = from;
        _dateTo = to;
        PeriodText.Text = $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_dateFrom == default) SetPeriod(DateTime.Today.AddMonths(-1), DateTime.Today);
        else Refresh();
    }

    public void Refresh()
    {
        if (!Core.Services.ServiceContainer.IsInitialized) return;

        var store = Core.Services.ServiceContainer.GetService<IDocumentStore>();
        var assignmentStore = Core.Services.ServiceContainer.GetOptionalService<IDocumentAssignmentStore>();
        var kpiService = Core.Services.ServiceContainer.GetOptionalService<IKpiService>();
        var riskService = Core.Services.ServiceContainer.GetOptionalService<IRiskScoringService>();

        var rows = PerformanceReport.GetDataByBranch(store, _dateFrom, _dateTo);
        var total = rows.Sum(r => r.Volume);
        var days = Math.Max(1, (_dateTo - _dateFrom).Days + 1);
        var throughput = total > 0 ? (decimal)total / days : 0;
        var cleared = rows.Sum(r => r.Cleared);
        var active = rows.Sum(r => r.Draft + r.Reviewed + r.ReadyForAudit + r.Issue + r.Cleared);
        var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
        var issueCount = rows.Sum(r => r.Issue);
        var issueRate = total > 0 ? (decimal)issueCount / total * 100 : 0;

        KpiCardsPanel.Children.Clear();
        AddKpiCard("Documents Processed", total.ToString(CultureInfo.InvariantCulture), "#0078D4");
        AddKpiCard("Clearing Rate", clearingRate.ToString("F1", CultureInfo.InvariantCulture) + "%", "#28A745");
        AddKpiCard("Issue Rate", issueRate.ToString("F1", CultureInfo.InvariantCulture) + "%", issueRate > 5 ? "#DC3545" : "#6C757D");
        AddKpiCard("Throughput/day", throughput.ToString("F1", CultureInfo.InvariantCulture), "#17A2B8");

        var plotModel = new PlotModel();
        var barSeries = new BarSeries { FillColor = OxyColor.FromRgb(0, 120, 212) };
        var categories = new List<string>();
        foreach (var r in rows.Take(10))
        {
            barSeries.Items.Add(new BarItem { Value = r.Volume });
            categories.Add(r.Name.Length > 15 ? r.Name[..15] + "…" : r.Name);
        }
        plotModel.Series.Add(barSeries);
        plotModel.Axes.Add(new CategoryAxis { Position = AxisPosition.Left, ItemsSource = categories });
        plotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0 });
        BranchChart.Model = plotModel;

        var branchItems = rows.Take(10).Select(r => new DrillDownItem("Branch", r.Name, $"{r.Name} — {r.Volume} docs")).ToList();
        BranchList.ItemsSource = branchItems;

        var trendModel = new PlotModel();
        var trendBars = new BarSeries { FillColor = OxyColor.FromRgb(0, 120, 212) };
        var monthLabels = new List<string>();
        var endMonth = new DateTime(_dateTo.Year, _dateTo.Month, 1);
        for (var i = 5; i >= 0; i--)
        {
            var monthStart = endMonth.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthDocs = store.ListDocuments(dateFrom: monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dateTo: monthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59", branch: null, section: null, limit: 50_000);
            trendBars.Items.Add(new BarItem { Value = monthDocs.Count });
            monthLabels.Add(monthStart.ToString("MMM yy", CultureInfo.CurrentCulture));
        }
        trendModel.Series.Add(trendBars);
        trendModel.Axes.Add(new CategoryAxis { Position = AxisPosition.Bottom, ItemsSource = monthLabels });
        trendModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0 });
        TrendChart.Model = trendModel;

        var riskItems = new List<DrillDownItem>();
        if (riskService != null)
        {
            var risks = riskService.GetRiskIndicators(store, assignmentStore, _dateFrom, _dateTo);
            foreach (var r in risks.Where(x => x.Level is RiskLevel.Critical or RiskLevel.High).Take(5))
                riskItems.Add(new DrillDownItem(r.EntityType, r.EntityName, $"{(r.Level == RiskLevel.Critical ? "🔴" : "🟠")} {r.EntityName}: {r.Reason}"));
        }
        var anomalyService = Core.Services.ServiceContainer.GetOptionalService<IReportAnomalyService>();
        if (anomalyService != null)
        {
            var anomalies = anomalyService.GetAnomalies(store, _dateFrom, _dateTo);
            foreach (var a in anomalies.Take(5))
                riskItems.Add(new DrillDownItem(a.EntityType, a.EntityName, $"⚠️ {a.EntityName}: {a.Reason}"));
        }
        if (riskItems.Count == 0) riskItems.Add(new DrillDownItem("", "", "No high-risk indicators or anomalies."));
        RiskList.ItemsSource = riskItems;

        var issues = new List<DrillDownItem>();
        foreach (var r in rows.Where(x => x.Issue > 0).OrderByDescending(x => x.Issue).Take(5))
            issues.Add(new DrillDownItem("Branch", r.Name, $"{r.Name}: {r.Issue} issue(s)"));
        if (issues.Count == 0) issues.Add(new DrillDownItem("", "", "No issues in this period."));
        IssuesList.ItemsSource = issues;
    }

    private void AddKpiCard(string title, string value, string color)
    {
        var card = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)),
            BorderThickness = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 16, 16),
            Width = 160,
            Height = 90
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Black, Margin = new Thickness(0, 4, 0, 0) });
        card.Child = sp;
        KpiCardsPanel.Children.Add(card);
    }

    private void BranchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BranchList.SelectedItem is DrillDownItem item && !string.IsNullOrEmpty(item.Branch))
            DrillDownRequested.Raise(new DrillDownRequest { Branch = item.Branch == "(No Branch)" ? "" : item.Branch, DateFrom = _dateFrom, DateTo = _dateTo });
    }

    private void IssuesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssuesList.SelectedItem is DrillDownItem item && !string.IsNullOrEmpty(item.Branch))
            DrillDownRequested.Raise(new DrillDownRequest { Branch = item.Branch == "(No Branch)" ? "" : item.Branch, DateFrom = _dateFrom, DateTo = _dateTo });
    }

    private void RiskList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RiskList.SelectedItem is not DrillDownItem item || string.IsNullOrEmpty(item.Branch)) return;
        var branch = item.EntityType == "Branch" ? (item.Branch == "(No Branch)" ? "" : item.Branch) : null;
        var section = item.EntityType == "Section" ? item.Branch : null;
        if (branch != null || section != null)
            DrillDownRequested.Raise(new DrillDownRequest { Branch = branch, Section = section, DateFrom = _dateFrom, DateTo = _dateTo });
    }

    private async void ExportPdfBtn_Click(object sender, RoutedEventArgs e)
    {
        var reportService = Core.Services.ServiceContainer.GetOptionalService<IReportService>();
        if (reportService == null) return;
        var config = new ReportConfig
        {
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            ReportType = ReportType.ExecutiveSummary,
            Format = ReportFormat.Pdf,
            IncludeCharts = true
        };
        try
        {
            var path = await reportService.GenerateAsync(config);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                MessageBox.Show($"Dashboard exported to:\n{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Log.Debug(ex, "Could not open file: {Path}", path); }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
