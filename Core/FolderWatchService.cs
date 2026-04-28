using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Import;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Core;

/// <summary>
/// Monitors folders for new files and imports them via IImportService.
/// Watched paths are persisted in user settings.
/// </summary>
public interface IFolderWatchService
{
    /// <summary>Paths currently being watched (read-only).</summary>
    IReadOnlyList<string> WatchedPaths { get; }

    /// <summary>Add a folder to watch and start monitoring. Idempotent.</summary>
    void AddWatch(string folderPath);

    /// <summary>Remove a folder from watch and stop monitoring.</summary>
    void RemoveWatch(string folderPath);

    /// <summary>True if the path is in the watched list and the watcher is active.</summary>
    bool IsWatching(string folderPath);

    /// <summary>Raised when a new file is detected and import has been started (args: full path).</summary>
    event Action<string>? FileDetected;

    /// <summary>Raised when a file has been imported (args: path, success).</summary>
    event Action<string, bool>? FileImported;
}

public class FolderWatchService : IFolderWatchService, IDisposable
{
    private const string WatchedFoldersKey = "WatchedFolders";
    private const int ImportDelayMs = 2500;

    private readonly ILogger _log = LoggingService.ForContext<FolderWatchService>();
    private readonly IImportService _importService;
    private readonly AppConfiguration _config;
    private readonly Dictionary<string, (FileSystemWatcher Watcher, CancellationTokenSource Cts)> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    public FolderWatchService(IImportService importService, AppConfiguration config)
    {
        _importService = importService;
        _config = config;
        LoadAndStartWatches();
    }

    public IReadOnlyList<string> WatchedPaths
    {
        get
        {
            lock (_lock)
            {
                return new ReadOnlyCollection<string>(_watchers.Keys.ToList());
            }
        }
    }

    public event Action<string>? FileDetected;
    public event Action<string, bool>? FileImported;

    public void AddWatch(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        var normalized = Path.GetFullPath(folderPath.Trim());
        if (!Directory.Exists(normalized))
        {
            _log.Warning("Cannot watch non-existent folder: {Path}", normalized);
            return;
        }

        lock (_lock)
        {
            if (_watchers.ContainsKey(normalized)) return;

            var cts = new CancellationTokenSource();
            var watcher = new FileSystemWatcher(normalized)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.*"
            };

            void OnCreated(object sender, FileSystemEventArgs e)
            {
                if (_disposed || string.IsNullOrEmpty(e.FullPath)) return;
                if (!_importService.IsSupportedFile(e.FullPath)) return;

                FileDetected?.Invoke(e.FullPath);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(ImportDelayMs, cts.Token).ConfigureAwait(false);
                        if (cts.Token.IsCancellationRequested) return;

                        var options = BuildDefaultOptions();
                        var result = await _importService.ImportFileAsync(e.FullPath, options, cts.Token).ConfigureAwait(false);
                        var success = result.SuccessCount > 0 && !result.HasErrors;
                        FileImported?.Invoke(e.FullPath, success);
                        if (success)
                            _log.Information("Folder watch imported: {Path}", e.FullPath);
                        else
                            _log.Warning("Folder watch import had issues for {Path}: {Errors}", e.FullPath, string.Join("; ", result.Errors));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Folder watch import failed: {Path}", e.FullPath);
                        FileImported?.Invoke(e.FullPath, false);
                    }
                }, cts.Token);
            }

            watcher.Created += OnCreated;
            watcher.EnableRaisingEvents = true;

            _watchers[normalized] = (watcher, cts);
            SaveWatchedPaths();
            _log.Information("Folder watch added: {Path}", normalized);
        }
    }

    public void RemoveWatch(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        var normalized = Path.GetFullPath(folderPath.Trim());

        lock (_lock)
        {
            if (!_watchers.Remove(normalized, out var pair)) return;

            try
            {
                pair.Cts.Cancel();
                pair.Cts.Dispose();
                pair.Watcher.EnableRaisingEvents = false;
                pair.Watcher.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error disposing watcher for {Path}", normalized);
            }

            SaveWatchedPaths();
            _log.Information("Folder watch removed: {Path}", normalized);
        }
    }

    public bool IsWatching(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;
        var normalized = Path.GetFullPath(folderPath.Trim());
        lock (_lock) return _watchers.ContainsKey(normalized);
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            foreach (var (watcher, cts) in _watchers.Values)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Error disposing file watcher during cleanup");
                }
            }
            _watchers.Clear();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void LoadAndStartWatches()
    {
        var paths = UserSettings.Get<List<string>>(WatchedFoldersKey);
        if (paths == null || paths.Count == 0) return;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var normalized = Path.GetFullPath(path.Trim());
                if (Directory.Exists(normalized) && !_watchers.ContainsKey(normalized))
                {
                    var cts = new CancellationTokenSource();
                    var watcher = new FileSystemWatcher(normalized)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        Filter = "*.*"
                    };

                    void OnCreated(object sender, FileSystemEventArgs e)
                    {
                        if (_disposed || string.IsNullOrEmpty(e.FullPath)) return;
                        if (!_importService.IsSupportedFile(e.FullPath)) return;

                        FileDetected?.Invoke(e.FullPath);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(ImportDelayMs, cts.Token).ConfigureAwait(false);
                                if (cts.Token.IsCancellationRequested) return;

                                var options = BuildDefaultOptions();
                                var result = await _importService.ImportFileAsync(e.FullPath, options, cts.Token).ConfigureAwait(false);
                                var success = result.SuccessCount > 0 && !result.HasErrors;
                                FileImported?.Invoke(e.FullPath, success);
                                if (success)
                                    _log.Information("Folder watch imported: {Path}", e.FullPath);
                                else
                                    _log.Warning("Folder watch import had issues for {Path}", e.FullPath);
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                _log.Warning(ex, "Folder watch import failed: {Path}", e.FullPath);
                                FileImported?.Invoke(e.FullPath, false);
                            }
                        }, cts.Token);
                    }

                    watcher.Created += OnCreated;
                    watcher.EnableRaisingEvents = true;
                    _watchers[normalized] = (watcher, cts);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Could not restore folder watch: {Path}", path);
            }
        }
    }

    private void SaveWatchedPaths()
    {
        var list = _watchers.Keys.ToList();
        UserSettings.Set(WatchedFoldersKey, list);
    }

    private ImportOptions BuildDefaultOptions()
    {
        return new ImportOptions
        {
            Branch = _config.CurrentUserBranch ?? Branches.Default,
            Section = Enums.Section.Individuals,
            BaseDirectory = _config.BaseDirectory ?? Defaults.GetDefaultBaseDir(),
            CopyToBaseDir = true,
            IncludeSubfolders = false,
            SkipDuplicates = true
        };
    }
}
