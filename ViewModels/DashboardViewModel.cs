using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.ViewModels;

/// <summary>
/// ViewModel for the Dashboard view. Handles data loading, caching, and date range filtering.
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    /// <summary>Admin Dashboard time range labels (must match <see cref="GetDateRange"/> keys).</summary>
    public static readonly IReadOnlyList<string> TimeRangeOptions = new[]
    {
        "Today",
        "This Week",
        "This Month",
        "Last Three Months",
        "Last Six Months",
        "Last Nine Months",
        "Last 1 Year",
        "All Time"
    };

    private readonly IDocumentStore _store;
    private readonly IDashboardCacheService _dashboardCache;
    private bool _isLoading;
    private List<Document> _filteredDocuments = new();
    private string _selectedTimeRange = "This Month";

    public DashboardViewModel(
        IDocumentStore store,
        IDashboardCacheService dashboardCache)
    {
        _store = store;
        _dashboardCache = dashboardCache;
        RefreshCommand = new RelayCommand(async () => await LoadDataAsync(SelectedTimeRange), () => !IsLoading);
    }

    /// <summary>
    /// Currently selected time range for dashboard metrics (e.g. "This Month").
    /// </summary>
    public string SelectedTimeRange
    {
        get => _selectedTimeRange;
        set
        {
            var v = value ?? "This Month";
            if (!TimeRangeOptions.Contains(v))
                v = "This Month";
            SetProperty(ref _selectedTimeRange, v);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Documents filtered by current date range. Set after LoadDataAsync.
    /// </summary>
    public List<Document> FilteredDocuments
    {
        get => _filteredDocuments;
        set => SetProperty(ref _filteredDocuments, value);
    }

    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Raised after LoadDataAsync completes so the view can update KPIs, activity feed, etc.
    /// </summary>
    public event Action? DataLoadCompleted;

    /// <summary>
    /// Loads dashboard data for the given time range. If selectedTimeRange is null, "This Month" is used.
    /// </summary>
    public async Task LoadDataAsync(string? selectedTimeRange)
    {
        IsLoading = true;
        try
        {
            // Always reload documents from the store so status / notes changes in Workspace are reflected in KPIs.
            _dashboardCache.Invalidate();

            var dateRange = GetDateRange(selectedTimeRange ?? "This Month");
            var cacheKey = $"dashboard:{dateRange.start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{dateRange.end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

            List<Document> filteredDocs;
            if (!_dashboardCache.TryGetDocuments(cacheKey, out filteredDocs))
            {
                filteredDocs = await Task.Run(() => _store.ListDocuments(
                    dateFrom: dateRange.start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    dateTo: dateRange.end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    limit: 10000));
                _dashboardCache.SetDocuments(cacheKey, filteredDocs);
            }
            FilteredDocuments = filteredDocs;
            DataLoadCompleted?.Invoke();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void InvalidateCache()
    {
        _dashboardCache.Invalidate();
    }

    public int GetTotalDocumentCount()
    {
        return _store.GetTotalDocumentCount();
    }

    /// <summary>Date range for <see cref="ListDocuments"/> (local calendar dates, inclusive end for <c>dateTo</c>).</summary>
    public static (DateTime? start, DateTime? end) GetDateRange(string selected) =>
        GetDateRange(selected, DateTime.Now);

    /// <summary>Testable overload using a fixed "now".</summary>
    public static (DateTime? start, DateTime? end) GetDateRange(string selected, DateTime now)
    {
        var d = now.Date;
        return selected switch
        {
            // Single calendar day (inclusive upper bound for yyyy-MM-dd in SQL).
            "Today" => (d, d),
            // Sunday-based week (unchanged behavior).
            "This Week" => (d.AddDays(-(int)d.DayOfWeek), d.AddDays(7 - (int)d.DayOfWeek)),
            "This Month" => (
                new DateTime(d.Year, d.Month, 1),
                new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month))),
            "Last Three Months" => (d.AddMonths(-3), d),
            "Last Six Months" => (d.AddMonths(-6), d),
            "Last Nine Months" => (d.AddMonths(-9), d),
            "Last 1 Year" => (d.AddYears(-1), d),
            "All Time" => (null, null),
            _ => (null, null)
        };
    }
}
