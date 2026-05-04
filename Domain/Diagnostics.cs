namespace WorkAudit.Domain;

/// <summary>Unified diagnostics snapshot for admin diagnostics UI.</summary>
public sealed class DiagnosticsSnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Healthy, Warning, or Critical aggregate.</summary>
    public string OverallHealthStatus { get; set; } = "Healthy";
    public HealthCheckResultSummary? HealthChecks { get; set; }
    public ErrorSummary ErrorSummary { get; set; } = new();
    public List<WorkflowIssue> WorkflowIssues { get; set; } = new();
    public List<ServiceStatusInfo> ServiceStatuses { get; set; } = new();
    public DatabaseMetrics DatabaseMetrics { get; set; } = new();
    public List<ConfigValidationItem> ConfigValidations { get; set; } = new();
    public ActivitySummary RecentActivity { get; set; } = new();
    public SessionMetrics SessionMetrics { get; set; } = new();
    public SystemStats SystemStats { get; set; } = new();
}

/// <summary>Serializable subset of health check for diagnostics export.</summary>
public sealed class HealthCheckResultSummary
{
    public DateTime TimestampUtc { get; set; }
    public bool IsHealthy { get; set; }
    public long CheckDurationMs { get; set; }
    public List<HealthCheckSummary> Checks { get; set; } = new();
}

public sealed class HealthCheckSummary
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsHealthy { get; set; }
    public string? Details { get; set; }
    public string Status => IsHealthy ? "OK" : "Issue";
}

public sealed class ErrorSummary
{
    public int ErrorCount24h { get; set; }
    public int WarningCount24h { get; set; }
    public int ErrorCount7d { get; set; }
    /// <summary>ERR/FTL in last 24h whose message suggests import / folder watch / merge pipeline issues.</summary>
    public int ImportRelatedErrorCount24h { get; set; }
    public Dictionary<string, int> ErrorsByComponent { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LogEntryModel> RecentErrors { get; set; } = new();
    public List<ErrorTrendPoint> TrendData { get; set; } = new();
}

/// <summary>Parsed Serilog line.</summary>
public sealed class LogEntryModel
{
    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = "";
    public string Component { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ExceptionBlock { get; set; }
    public int ProcessId { get; set; }
    public string MachineName { get; set; } = "";
}

public sealed class ErrorTrendPoint
{
    public DateTime HourUtc { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    /// <summary>Preformatted hour bucket for grids (UTC).</summary>
    public string HourLabel => HourUtc.ToString("yyyy-MM-dd HH:00", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class WorkflowIssue
{
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public int? DocumentId { get; set; }
    public string? DocumentUuid { get; set; }
    public int? AssignmentId { get; set; }
    public string Description { get; set; } = "";
    public string? RecommendedAction { get; set; }
    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class ServiceStatusInfo
{
    public string ServiceName { get; set; } = "";
    /// <summary>Running, Stopped, Warning, Error, Unknown</summary>
    public string Status { get; set; } = "Unknown";
    public DateTime? LastActivityUtc { get; set; }
    public string? Details { get; set; }
}

public sealed class DatabaseMetrics
{
    public bool IsConnected { get; set; }
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public long AvgQueryTimeMs { get; set; }
    public int SlowQueriesCount { get; set; }
    /// <summary>ERR/FTL lines in the last 24h that mention Oracle/timeouts (supplements probe-based metrics).</summary>
    public int LogDatabaseIssueCount24h { get; set; }
    /// <summary>Total rows in <c>v$session</c> when the query succeeds (requires Oracle catalog view privilege).</summary>
    public int? OracleVSessionTotal { get; set; }
    /// <summary><c>v$session</c> rows with <c>STATUS='ACTIVE'</c> when available.</summary>
    public int? OracleVSessionActive { get; set; }
    public string SchemaVersion { get; set; } = "";
    public Dictionary<string, long> TableRowCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; set; } = new();
}

public sealed class ConfigValidationItem
{
    public string ConfigKey { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsValid { get; set; }
    /// <summary>Valid, Missing, Invalid, Unreachable</summary>
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string Severity { get; set; } = "Info";
}

public sealed class ActivitySummary
{
    public DateTime? LastDocumentImportUtc { get; set; }
    public int DocumentsImportedToday { get; set; }
    public DateTime? LastOcrCompletionUtc { get; set; }
    public int OcrCompletedToday { get; set; }
    public DateTime? LastBackupUtc { get; set; }
    public string? LastBackupStatus { get; set; }
    public DateTime? LastReportGeneratedUtc { get; set; }
    public string? LastReportType { get; set; }
    public int ActiveUsersToday { get; set; }
}

public sealed class SessionMetrics
{
    public int ActiveSessions { get; set; }
    public int FailedLoginsLast24h { get; set; }
    public int LockedOutUsers { get; set; }
    public List<string> ActiveUsernames { get; set; } = new();
    public DateTime? OldestActiveSessionUtc { get; set; }
}

public sealed class PerformanceMetricModel
{
    public DateTime TimestampUtc { get; set; }
    public string Operation { get; set; } = "";
    public long DurationMs { get; set; }
    public int? ItemCount { get; set; }
    public long? BytesProcessed { get; set; }
    public bool IsSlow { get; set; }
}

public sealed class SystemStats
{
    public long TotalDocuments { get; set; }
    public Dictionary<string, long> DocumentsByStatus { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long PendingAssignments { get; set; }
    public long OverdueAssignments { get; set; }
    public long FailedOcrCount { get; set; }
    public long OrphanedFiles { get; set; }
    public long MissingFiles { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public long TotalBranches { get; set; }
    public long TotalDocumentTypes { get; set; }
}

/// <summary>Single metric row for overview grid binding.</summary>
public sealed class DiagnosticMetricRow
{
    public string Metric { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Activity row for overview grid.</summary>
public sealed class DiagnosticActivityRow
{
    public string Activity { get; set; } = "";
    public string LastOccurrence { get; set; } = "";
    public string CountToday { get; set; } = "";
}

/// <summary>Database metric row for UI binding.</summary>
public sealed class DatabaseMetricRow
{
    public string Metric { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Table size row for UI.</summary>
public sealed class TableSizeRow
{
    public string TableName { get; set; } = "";
    public long RowCount { get; set; }
    public string Growth { get; set; } = "—";
}
