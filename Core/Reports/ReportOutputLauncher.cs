using System.Diagnostics;
using System.IO;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Opens a generated report path in the shell. Handles single files and folder outputs
/// (e.g. per-branch PDF export without zip), since <see cref="File.Exists"/> is false for directories.
/// </summary>
public static class ReportOutputLauncher
{
    private static readonly ILogger Log = LoggingService.ForContext(typeof(ReportOutputLauncher));

    /// <summary>
    /// Opens a file with its default app, or a directory in Windows Explorer.
    /// </summary>
    /// <param name="path">File or folder path.</param>
    /// <param name="errorMessage">Set when the method returns false.</param>
    /// <returns>True if a process was started.</returns>
    public static bool TryOpen(string path, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path is empty.";
            return false;
        }

        var trimmed = path.Trim();

        try
        {
            if (File.Exists(trimmed))
            {
                Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true });
                return true;
            }

            if (Directory.Exists(trimmed))
            {
                // explorer.exe expects a quoted path when it contains spaces
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}\"",
                    UseShellExecute = true
                });
                return true;
            }

            errorMessage = $"File or folder not found:\n{trimmed}";
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open report output: {Path}", trimmed);
            errorMessage = string.IsNullOrWhiteSpace(ex.Message) ? "Could not open the file or folder." : ex.Message;
            return false;
        }
    }
}
