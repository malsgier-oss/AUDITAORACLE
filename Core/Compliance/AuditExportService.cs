using System.IO;
using System.Text;
using Serilog;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// Exports audit trail logs for regulators and compliance.
/// </summary>
public interface IAuditExportService
{
    Task<string> ExportToCsvAsync(DateTime from, DateTime to, string? userId = null, string? category = null, bool archivedOnly = false, int limit = 10000);
    Task ExportToFileAsync(string filePath, DateTime from, DateTime to, string? userId = null, string? category = null, bool archivedOnly = false, int limit = 10000);
}

public class AuditExportService : IAuditExportService
{
    private readonly ILogger _log = LoggingService.ForContext<AuditExportService>();
    private readonly IAuditLogStore _auditStore;
    private readonly IAuditTrailService _auditTrail;

    public AuditExportService(IAuditLogStore auditStore, IAuditTrailService auditTrail)
    {
        _auditStore = auditStore;
        _auditTrail = auditTrail;
    }

    public async Task<string> ExportToCsvAsync(DateTime from, DateTime to, string? userId = null, string? category = null, bool archivedOnly = false, int limit = 10000)
    {
        var fromUtc = AuditTimeHelper.ToUtcFromDateUtcPlus2(from);
        var toUtc = AuditTimeHelper.ToUtcToDateUtcPlus2(to);
        var entries = _auditStore.Query(fromUtc, toUtc, userId, null, category, archivedOnly, limit);
        var sb = new StringBuilder();

        sb.AppendLine("Timestamp (UTC+2),UserId,Username,UserRole,Action,Category,EntityType,EntityId,OldValue,NewValue,Details,Success");

        foreach (var e in entries)
        {
            var line = string.Join(",",
                EscapeCsv(AuditTimeHelper.FormatForDisplay(e.Timestamp)),
                EscapeCsv(e.UserId),
                EscapeCsv(e.Username),
                EscapeCsv(e.UserRole),
                EscapeCsv(e.Action),
                EscapeCsv(e.Category),
                EscapeCsv(e.EntityType),
                EscapeCsv(e.EntityId ?? ""),
                EscapeCsv(e.OldValue ?? ""),
                EscapeCsv(e.NewValue ?? ""),
                EscapeCsv(e.Details ?? ""),
                e.Success ? "Yes" : "No");
            sb.AppendLine(line);
        }

        await _auditTrail.LogSystemActionAsync("AuditLogExported", $"Exported {entries.Count} entries from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        _log.Information("Exported {Count} audit log entries to CSV", entries.Count);
        return sb.ToString();
    }

    public async Task ExportToFileAsync(string filePath, DateTime from, DateTime to, string? userId = null, string? category = null, bool archivedOnly = false, int limit = 10000)
    {
        var csv = await ExportToCsvAsync(from, to, userId, category, archivedOnly, limit);
        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
