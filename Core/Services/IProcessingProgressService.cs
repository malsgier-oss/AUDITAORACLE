namespace WorkAudit.Core.Services;

/// <summary>
/// Reports processing progress for display in the main window's inline progress bar.
/// Callers (e.g. ProcessingView) report progress; MainWindow subscribes and shows/hides the bar.
/// </summary>
public interface IProcessingProgressService
{
    /// <summary>Raised when progress state changes. Subscribe on UI thread.</summary>
    event EventHandler<ProcessingProgressEventArgs>? ProgressChanged;

    /// <summary>Start a new progress session. Hides any previous session.</summary>
    /// <param name="total">Total items (or 0 for indeterminate).</param>
    /// <param name="message">Initial message.</param>
    /// <returns>CancellationToken for cancel support.</returns>
    System.Threading.CancellationToken Start(int total, string message);

    /// <summary>Report progress.</summary>
    void Report(int current, int total, string message);

    /// <summary>Mark progress complete and hide the bar.</summary>
    void Complete();

    /// <summary>Request cancellation. Callers should check token and stop.</summary>
    void Cancel();
}

/// <summary>Event args for processing progress updates.</summary>
public class ProcessingProgressEventArgs : EventArgs
{
    public bool IsVisible { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = "";
    public bool IsIndeterminate => Total <= 0;
    public double Percent => Total > 0 ? (Current * 100.0 / Total) : 0;
}
