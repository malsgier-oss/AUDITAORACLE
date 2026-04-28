using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Tracks report access (view, export, print) for compliance audit trail.
/// </summary>
public interface IReportDistributionService
{
    void LogView(string reportPath, string reportType, string userId, string username);
    void LogExport(string reportPath, string reportType, string userId, string username, string? format = null);
    void LogPrint(string reportPath, string reportType, string userId, string username);
}

public class ReportDistributionService : IReportDistributionService
{
    private readonly IReportDistributionStore _store;

    public ReportDistributionService(IReportDistributionStore store)
    {
        _store = store;
    }

    public void LogView(string reportPath, string reportType, string userId, string username)
    {
        _store.Log(reportPath, reportType, "View", userId, username);
    }

    public void LogExport(string reportPath, string reportType, string userId, string username, string? format = null)
    {
        _store.Log(reportPath, reportType, "Export", userId, username, format);
    }

    public void LogPrint(string reportPath, string reportType, string userId, string username)
    {
        _store.Log(reportPath, reportType, "Print", userId, username);
    }
}
