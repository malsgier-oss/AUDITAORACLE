using System.IO;
using System.Diagnostics;
using System.Globalization;
using Serilog;
using Serilog.Events;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>
/// Centralized logging service using Serilog.
/// Provides structured logging with file rotation, performance metrics, and health monitoring.
/// </summary>
public static class LoggingService
{
    private static bool _initialized;
    private static readonly object _lock = new();
    private static readonly Stopwatch _uptime = Stopwatch.StartNew();

    public static string LogDirectory { get; private set; } = "";

    public static void Initialize(string? logDirectory = null)
    {
        lock (_lock)
        {
            if (_initialized) return;

            LogDirectory = logDirectory ?? GetDefaultLogDirectory();
            Directory.CreateDirectory(LogDirectory);

            var logPath = Path.Combine(LogDirectory, "workaudit-.log");
            var perfLogPath = Path.Combine(LogDirectory, "performance-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.WithProperty("UserName", Environment.UserName)
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{MachineName}/{ProcessId}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .WriteTo.File(
                    perfLogPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message:lj}{NewLine}",
                    shared: true)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture,
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .CreateLogger();

            _initialized = true;

            Log.Information("=== WorkAudit Started ===");
            Log.Information("Version: {Version}", Config.Defaults.AppVersion);
            Log.Information("Log Directory: {LogDirectory}", LogDirectory);
            Log.Information("Environment: Machine={Machine}, User={User}, OS={OS}", 
                Environment.MachineName, Environment.UserName, Environment.OSVersion);
        }
    }

    public static void Shutdown()
    {
        Log.Information("=== WorkAudit Shutdown ===");
        Log.Information("Uptime: {Uptime}", _uptime.Elapsed);
        Log.CloseAndFlush();
    }

    public static ILogger ForContext<T>() => Log.ForContext<T>();
    public static ILogger ForContext(Type type) => Log.ForContext(type);
    public static ILogger ForContext(string sourceContext) => Log.ForContext("SourceContext", sourceContext);

    private static string GetDefaultLogDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WORKAUDIT", "Logs");
    }

    public static void LogPerformanceMetric(string operation, long durationMs, int? itemCount = null, long? bytesProcessed = null)
    {
        var message = $"PERF: {operation} completed in {durationMs}ms";
        if (itemCount.HasValue)
            message += $" ({itemCount.Value} items)";
        if (bytesProcessed.HasValue)
            message += $" [{FormatBytes(bytesProcessed.Value)}]";
        
        Log.Information(message);
    }

    public static void LogHealthCheck(string component, bool healthy, string? details = null)
    {
        var level = healthy ? LogEventLevel.Information : LogEventLevel.Warning;
        var message = $"HEALTH: {component} is {(healthy ? "healthy" : "unhealthy")}";
        if (!string.IsNullOrEmpty(details))
            message += $" - {details}";
        
        Log.Write(level, message);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F2} {sizes[order]}";
    }

    // Convenience methods
    public static void Debug(string message, params object[] args) => Log.Debug(message, args);
    public static void Info(string message, params object[] args) => Log.Information(message, args);
    public static void Warning(string message, params object[] args) => Log.Warning(message, args);
    public static void Error(string message, params object[] args) => Log.Error(message, args);
    public static void Error(Exception ex, string message, params object[] args) => Log.Error(ex, message, args);
    public static void Fatal(string message, params object[] args) => Log.Fatal(message, args);
    public static void Fatal(Exception ex, string message, params object[] args) => Log.Fatal(ex, message, args);
}

/// <summary>
/// System health monitoring service for production environments.
/// Performs periodic checks on database, storage, memory, and critical services.
/// </summary>
public interface IHealthCheckService
{
    Task<HealthCheckResult> PerformHealthCheckAsync();
    List<HealthCheckResult> GetHealthHistory(int limit = 100);
}

public class HealthCheckService : IHealthCheckService
{
    private readonly ILogger _log = LoggingService.ForContext<HealthCheckService>();
    private readonly AppConfiguration _config;
    private readonly IDocumentStore _documentStore;
    private readonly IUserStore _userStore;
    private readonly List<HealthCheckResult> _history = new();

    public HealthCheckService(AppConfiguration config, IDocumentStore documentStore, IUserStore userStore)
    {
        _config = config;
        _documentStore = documentStore;
        _userStore = userStore;
    }

    public async Task<HealthCheckResult> PerformHealthCheckAsync()
    {
        var sw = Stopwatch.StartNew();
        var result = new HealthCheckResult { Timestamp = DateTime.UtcNow };

        await Task.Run(() =>
        {
            CheckDatabase(result);
            CheckStorage(result);
            CheckMemory(result);
            CheckDiskSpace(result);
        });

        sw.Stop();
        result.CheckDurationMs = sw.ElapsedMilliseconds;
        result.IsHealthy = result.Checks.TrueForAll(c => c.IsHealthy);

        _history.Add(result);
        if (_history.Count > 500)
            _history.RemoveRange(0, 100);

        LoggingService.LogHealthCheck("System", result.IsHealthy, 
            result.IsHealthy ? $"All checks passed in {result.CheckDurationMs}ms" 
                             : $"{result.Checks.Count(c => !c.IsHealthy)} checks failed");

        return result;
    }

    public List<HealthCheckResult> GetHealthHistory(int limit = 100)
    {
        return _history.TakeLast(limit).ToList();
    }

    private void CheckDatabase(HealthCheckResult result)
    {
        var check = new HealthCheck { Name = "Database", Category = "Storage" };
        try
        {
            var sw = Stopwatch.StartNew();
            var userCount = _userStore.Count();
            var docCount = _documentStore.Count();
            sw.Stop();

            check.IsHealthy = true;
            check.Details = $"Users: {userCount}, Documents: {docCount}, Query time: {sw.ElapsedMilliseconds}ms";
            
            if (sw.ElapsedMilliseconds > 5000)
            {
                check.IsHealthy = false;
                check.Details += " (SLOW QUERY WARNING)";
            }
        }
        catch (Exception ex)
        {
            check.IsHealthy = false;
            check.Details = $"Database error: {ex.Message}";
            _log.Error(ex, "Health check failed for database");
        }
        result.Checks.Add(check);
    }

    private void CheckStorage(HealthCheckResult result)
    {
        var check = new HealthCheck { Name = "File Storage", Category = "Storage" };
        try
        {
            var baseDir = _config.BaseDirectory;
            var attachmentsDir = Path.Combine(baseDir, "attachments");

            var exists = Directory.Exists(baseDir) && Directory.Exists(attachmentsDir);
            check.IsHealthy = exists;
            
            if (exists)
            {
                var fileCount = Directory.GetFiles(attachmentsDir, "*.*", SearchOption.AllDirectories).Length;
                var totalSize = Directory.GetFiles(attachmentsDir, "*.*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                check.Details = $"{fileCount} files, {FormatBytes(totalSize)} total";
            }
            else
            {
                check.Details = "Storage directories missing";
            }
        }
        catch (Exception ex)
        {
            check.IsHealthy = false;
            check.Details = $"Storage error: {ex.Message}";
            _log.Error(ex, "Health check failed for storage");
        }
        result.Checks.Add(check);
    }

    private void CheckMemory(HealthCheckResult result)
    {
        var check = new HealthCheck { Name = "Memory", Category = "System" };
        try
        {
            var process = Process.GetCurrentProcess();
            var workingSetMB = process.WorkingSet64 / (1024 * 1024);
            var privateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024);
            
            check.IsHealthy = workingSetMB < 2048;
            check.Details = $"Working Set: {workingSetMB} MB, Private: {privateMemoryMB} MB";
            
            if (workingSetMB > 2048)
                check.Details += " (HIGH MEMORY WARNING)";
        }
        catch (Exception ex)
        {
            check.IsHealthy = false;
            check.Details = $"Memory check error: {ex.Message}";
        }
        result.Checks.Add(check);
    }

    private void CheckDiskSpace(HealthCheckResult result)
    {
        var check = new HealthCheck { Name = "Disk Space", Category = "System" };
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_config.BaseDirectory) ?? "C:\\");
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            var totalSpaceGB = drive.TotalSize / (1024.0 * 1024 * 1024);
            var freePercent = (freeSpaceGB / totalSpaceGB) * 100;

            check.IsHealthy = freeSpaceGB > 10;
            check.Details = $"{freeSpaceGB:F2} GB free ({freePercent:F1}% of {totalSpaceGB:F2} GB)";
            
            if (freeSpaceGB < 10)
                check.Details += " (LOW DISK SPACE WARNING)";
            else if (freeSpaceGB < 50)
                check.Details += " (disk space getting low)";
        }
        catch (Exception ex)
        {
            check.IsHealthy = false;
            check.Details = $"Disk check error: {ex.Message}";
        }
        result.Checks.Add(check);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F2} {sizes[order]}";
    }
}

public class HealthCheck
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsHealthy { get; set; }
    public string? Details { get; set; }
}

public class HealthCheckResult
{
    public DateTime Timestamp { get; set; }
    public bool IsHealthy { get; set; }
    public long CheckDurationMs { get; set; }
    public List<HealthCheck> Checks { get; set; } = new();
}
