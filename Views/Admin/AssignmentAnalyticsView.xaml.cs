using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class AssignmentAnalyticsView : UserControl
{
    private IDocumentAssignmentService? _assignmentService;
    private IUserStore? _userStore;

    public AssignmentAnalyticsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;
        _assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        _userStore = ServiceContainer.GetService<IUserStore>();

        StatusFilterCombo.Items.Clear();
        StatusFilterCombo.Items.Add("Active (Pending + In Progress)");
        StatusFilterCombo.Items.Add("All (incl. Completed)");
        StatusFilterCombo.SelectedIndex = 0;

        RefreshChart();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshChart();
    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshChart();

    private void RefreshChart()
    {
        if (_assignmentService == null || _userStore == null) return;

        var includeCompleted = StatusFilterCombo.SelectedIndex == 1;
        var assignments = _assignmentService.GetAllAssignments(null, null);

        var statuses = includeCompleted
            ? new[] { AssignmentStatus.Pending, AssignmentStatus.InProgress, AssignmentStatus.Completed }
            : new[] { AssignmentStatus.Pending, AssignmentStatus.InProgress };

        var byUser = assignments
            .Where(a => statuses.Contains(a.Status))
            .GroupBy(a => a.AssignedToUsername)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToList();

        var plot = new PlotModel { Title = "" };
        if (byUser.Count == 0)
        {
            plot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, Maximum = 1 });
            plot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0, Maximum = 1 });
            WorkloadChart.Model = plot;
            return;
        }
        plot.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            ItemsSource = byUser.Select(g => g.Key).ToArray(),
            FontSize = 11
        });
        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 0,
            Title = "Count"
        });

        var series = new BarSeries
        {
            ItemsSource = byUser.Select(g => new BarItem(g.Count())).ToArray(),
            FillColor = OxyColor.FromRgb(14, 99, 156),
            StrokeColor = OxyColor.FromRgb(10, 70, 110),
            StrokeThickness = 1
        };
        plot.Series.Add(series);

        WorkloadChart.Model = plot;
    }
}
