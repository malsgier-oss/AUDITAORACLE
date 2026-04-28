using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class AssignmentCalendarView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<AssignmentCalendarView>();
    private IDocumentAssignmentService? _assignmentService;
    private IDocumentStore? _documentStore;
    private IUserStore? _userStore;
    private DateTime _viewMonth = DateTime.Today;

    public AssignmentCalendarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;
        _assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        _documentStore = ServiceContainer.GetService<IDocumentStore>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        RefreshCalendar();
    }

    private void PrevMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(-1);
        RefreshCalendar();
    }

    private void NextMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(1);
        RefreshCalendar();
    }

    private void TodayBtn_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = DateTime.Today;
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        MonthYearText.Text = _viewMonth.ToString("MMMM yyyy");
        if (_assignmentService == null || _documentStore == null) return;

        var assignments = _assignmentService.GetAllAssignments(null, null);
        var start = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var days = new ObservableCollection<CalendarDayItem>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dayStr = d.ToString("yyyy-MM-dd");
            var dayAssignments = assignments
                .Where(a => a.DueDate == dayStr && a.Status != AssignmentStatus.Cancelled)
                .Select(a =>
                {
                    var gr = _documentStore.GetResult(a.DocumentId);
                    if (!gr.IsSuccess)
                        Log.Warning("Could not load document {Id} for calendar: {Error}", a.DocumentId, gr.Error);
                    var docName = gr.IsSuccess
                        ? Path.GetFileName(gr.Value!.FilePath) ?? gr.Value.Uuid
                        : $"Document #{a.DocumentId}";
                    var isOverdue = _assignmentService.IsOverdue(a);
                    return new CalendarAssignmentItem(a, docName, isOverdue);
                })
                .ToList();
            days.Add(new CalendarDayItem(d, dayAssignments));
        }

        CalendarDaysPanel.ItemsSource = days;
    }

    private sealed class CalendarDayItem
    {
        public DateTime Date { get; }
        public string DateDisplay { get; }
        public ObservableCollection<CalendarAssignmentItem> Assignments { get; }

        public CalendarDayItem(DateTime date, List<CalendarAssignmentItem> assignments)
        {
            Date = date;
            DateDisplay = date.ToString("ddd, MMM d");
            Assignments = new ObservableCollection<CalendarAssignmentItem>(assignments);
        }
    }

    public sealed class CalendarAssignmentItem
    {
        public DocumentAssignment Assignment { get; }
        public string DocumentName { get; }
        public string AssignedToUsername => Assignment.AssignedToUsername;
        public string PriorityDisplay => (Assignment.Priority switch { AssignmentPriority.Urgent => "!!", AssignmentPriority.High => "↑", AssignmentPriority.Low => "↓", _ => "•" }) + " " + Assignment.Priority;
        public string StatusDisplay => Assignment.Status;
        private bool IsOverdue { get; }

        public CalendarAssignmentItem(DocumentAssignment a, string docName, bool isOverdue)
        {
            Assignment = a;
            DocumentName = docName;
            IsOverdue = isOverdue;
        }
    }
}
