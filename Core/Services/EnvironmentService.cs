using System.IO;
using Serilog;
using WorkAudit.Core.Camera;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>
/// Service for detecting and validating the runtime environment.
/// Checks for required dependencies (webcams, etc.)
/// </summary>
public interface IEnvironmentService
{
    EnvironmentInfo GetEnvironmentInfo();
    Task<EnvironmentInfo> GetEnvironmentInfoAsync();
    bool IsWebcamAvailable();
    List<string> GetAvailableWebcams();
    Task<bool> ValidateEnvironmentAsync();
}

public class EnvironmentService : IEnvironmentService
{
    private readonly ILogger _log = LoggingService.ForContext<EnvironmentService>();
    private readonly IConfigStore _configStore;
    private readonly ICameraService _cameraService;
    private EnvironmentInfo? _cachedInfo;
    private readonly object _lock = new();

    public EnvironmentService(IConfigStore configStore, ICameraService cameraService)
    {
        _configStore = configStore;
        _cameraService = cameraService;
    }

    public EnvironmentInfo GetEnvironmentInfo()
    {
        return Task.Run(GetEnvironmentInfoAsync).GetAwaiter().GetResult();
    }

    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync()
    {
        lock (_lock)
        {
            if (_cachedInfo != null) return _cachedInfo;
        }

        var info = new EnvironmentInfo
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            OSVersion = Environment.OSVersion.ToString(),
            Is64Bit = Environment.Is64BitOperatingSystem,
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = Environment.Version.ToString(),
            WorkingDirectory = Environment.CurrentDirectory,
            WebcamAvailable = IsWebcamAvailable(),
            AvailableWebcams = GetAvailableWebcams(),
            AvailableDiskSpaceGB = GetAvailableDiskSpace(),
            TotalMemoryGB = GetTotalMemory()
        };

        lock (_lock)
        {
            _cachedInfo = info;
        }

        _log.Information("Environment detected: OS={OS}, 64bit={Is64Bit}",
            info.OSVersion, info.Is64Bit);

        return info;
    }

    public bool IsWebcamAvailable()
    {
        return GetAvailableWebcams().Count > 0;
    }

    public List<string> GetAvailableWebcams()
    {
        try
        {
            var cameras = _cameraService.GetAvailableCameras();
            return cameras.ConvertAll(c => c.Name);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error detecting webcams");
            return new List<string>();
        }
    }

    public async Task<bool> ValidateEnvironmentAsync()
    {
        var info = await GetEnvironmentInfoAsync();
        var issues = new List<string>();
        if (info.AvailableDiskSpaceGB < 1)
            issues.Add("Low disk space (less than 1 GB available).");
        if (!info.WebcamAvailable)
            _log.Warning("No webcam detected - document capture will be limited to file import.");
        if (issues.Count > 0)
        {
            foreach (var issue in issues)
                _log.Error("Environment issue: {Issue}", issue);
            return false;
        }
        _log.Information("Environment validation passed");
        return true;
    }

    private double GetAvailableDiskSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:");
            return drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
        }
        catch { return -1; }
    }

    private double GetTotalMemory()
    {
        try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024); }
        catch { return -1; }
    }
}

public class EnvironmentInfo
{
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OSVersion { get; set; } = "";
    public bool Is64Bit { get; set; }
    public int ProcessorCount { get; set; }
    public string DotNetVersion { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool WebcamAvailable { get; set; }
    public List<string> AvailableWebcams { get; set; } = new();
    public double AvailableDiskSpaceGB { get; set; }
    public double TotalMemoryGB { get; set; }
}
