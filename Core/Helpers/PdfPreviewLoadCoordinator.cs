using System;
using System.Windows.Threading;
using PdfiumViewer;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Avoids PdfiumViewer raising ObjectDisposedException on <c>PdfPage</c> when
/// <see cref="PDFViewer.Source"/> changes quickly: clears the document first, waits for
/// two <see cref="DispatcherPriority.ApplicationIdle"/> passes so unload/layout can finish,
/// then assigns the new path. Callers should keep the viewer hidden until
/// <paramref name="onSourceApplied"/> runs (non-null path only).
/// </summary>
public sealed class PdfPreviewLoadCoordinator
{
    private static readonly ILogger Log = LoggingService.ForContext<PdfPreviewLoadCoordinator>();

    private int _generation;

    public void SetSource(PDFViewer? viewer, string? filePath, Action? onSourceApplied = null)
    {
        if (viewer == null) return;

        var gen = ++_generation;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            TryClearSource(viewer);
            return;
        }

        TryClearSource(viewer);
        var dispatcher = viewer.Dispatcher;
        // First idle: let PdfiumViewer tear down pages after Source=null.
        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (gen != _generation) return;
                // Second idle: avoid touching PdfPage while prior document is still disposing.
                dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                    {
                        if (gen != _generation) return;
                        if (!TrySetSource(viewer, filePath)) return;
                        onSourceApplied?.Invoke();
                    }));
            }));
    }

    private static bool IsPdfPageDisposed(ObjectDisposedException ex) =>
        ex.ObjectName != null &&
        ex.ObjectName.Contains("PdfPage", StringComparison.OrdinalIgnoreCase);

    private static void TryClearSource(PDFViewer viewer)
    {
        try
        {
            viewer.Source = null;
        }
        catch (ObjectDisposedException ex) when (IsPdfPageDisposed(ex))
        {
            Log.Warning(ex, "Pdf preview clear skipped (disposed PdfPage)");
        }
    }

    private static bool TrySetSource(PDFViewer viewer, string filePath)
    {
        try
        {
            viewer.Source = filePath;
            return true;
        }
        catch (ObjectDisposedException ex) when (IsPdfPageDisposed(ex))
        {
            Log.Warning(ex, "Pdf preview load skipped (disposed PdfPage)");
            return false;
        }
    }
}
