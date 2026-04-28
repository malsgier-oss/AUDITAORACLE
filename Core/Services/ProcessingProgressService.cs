using System.Windows;
using System.Windows.Threading;

namespace WorkAudit.Core.Services;

/// <summary>
/// Implementation of IProcessingProgressService. Raises ProgressChanged on the UI dispatcher.
/// Throttles Report() to avoid flooding the UI thread during bulk processing.
/// </summary>
public class ProcessingProgressService : IProcessingProgressService
{
    private readonly Dispatcher _dispatcher;
    private readonly object _ctsLock = new();
    private System.Threading.CancellationTokenSource? _cts;
    private const int ThrottleMs = 150;
    private DateTime _lastRaised = DateTime.MinValue;
    private int _lastReportedPercent = -1;

    public event EventHandler<ProcessingProgressEventArgs>? ProgressChanged;

    public ProcessingProgressService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc />
    public System.Threading.CancellationToken Start(int total, string message)
    {
        System.Threading.CancellationToken token;
        lock (_ctsLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new System.Threading.CancellationTokenSource();
            token = _cts.Token;
            _lastReportedPercent = -1;
            _lastRaised = DateTime.MinValue;
        }

        Raise(new ProcessingProgressEventArgs
        {
            IsVisible = true,
            Current = 0,
            Total = total,
            Message = message
        });
        return token;
    }

    /// <inheritdoc />
    public void Report(int current, int total, string message)
    {
        var percent = total > 0 ? (int)(current * 100.0 / total) : 0;
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRaised).TotalMilliseconds;
        var percentChanged = percent != _lastReportedPercent;
        var isLast = current >= total;
        if (!isLast && !percentChanged && elapsed < ThrottleMs)
            return;
        _lastReportedPercent = percent;
        _lastRaised = now;
        Raise(new ProcessingProgressEventArgs
        {
            IsVisible = true,
            Current = current,
            Total = total,
            Message = message
        });
    }

    /// <inheritdoc />
    public void Complete()
    {
        lock (_ctsLock)
        {
            _cts?.Dispose();
            _cts = null;
        }

        Raise(new ProcessingProgressEventArgs { IsVisible = false, Current = 0, Total = 0, Message = "" });
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_ctsLock)
        {
            _cts?.Cancel();
        }
    }

    private void Raise(ProcessingProgressEventArgs e)
    {
        var handler = ProgressChanged;
        if (handler == null) return;

        if (_dispatcher.CheckAccess())
            handler(this, e);
        else
            _dispatcher.BeginInvoke(() => handler(this, e));
    }
}
