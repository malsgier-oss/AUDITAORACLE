using System;
using WorkAudit.Domain;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Storage;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// When a document is in the audit workspace (<see cref="Enums.Status.ReadyForAudit"/>), enqueue background OCR when text is missing
/// or (for PDFs, when configured) when embedded extraction looks too sparse to be useful.
/// </summary>
public static class DocumentWorkspaceOcr
{
    public const string SettingWorkspaceAutoBackfill = "ocr_workspace_auto_backfill";
    public const string SettingWorkspaceBackfillBatchSize = "ocr_workspace_backfill_batch_size";
    public const string SettingWorkspaceAutoOnSelect = "ocr_workspace_auto_on_select";
    public const string SettingPdfMinCharsForSkip = "ocr_pdf_min_chars_for_skip";

    /// <summary>
    /// Enqueues a background OCR task when the document is Ready for Audit and needs OCR per policy.
    /// </summary>
    /// <param name="config">When <c>null</c>, only enqueues if <see cref="Document.OcrText"/> is empty (legacy behavior).</param>
    public static void TryEnqueueOcrIfNeeded(Document? doc, IOcrService? ocrService, IConfigStore? config = null)
    {
        if (doc == null || ocrService == null) return;
        if (string.IsNullOrEmpty(doc.FilePath)) return;
        if (!string.Equals(doc.Status, Enums.Status.ReadyForAudit, StringComparison.Ordinal)) return;
        if (!NeedsBackgroundOcr(doc, config)) return;
        ocrService.EnqueueOcrTask(doc);
    }

    /// <summary>
    /// Returns true when background OCR should run: no usable text, or PDF with low-signal embedded text (when <paramref name="config"/> is supplied).
    /// </summary>
    public static bool NeedsBackgroundOcr(Document doc, IConfigStore? config)
    {
        if (string.IsNullOrEmpty(doc.FilePath)) return false;
        if (string.IsNullOrWhiteSpace(doc.OcrText)) return true;
        return IsPdfLowSignalText(doc, config);
    }

    private static bool IsPdfLowSignalText(Document doc, IConfigStore? config)
    {
        if (config == null) return false;
        var path = doc.FilePath;
        if (string.IsNullOrEmpty(path)) return false;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        var minChars = config.GetSettingInt(SettingPdfMinCharsForSkip, 50);
        if (minChars <= 0) return false;
        return CountAlphanumeric(doc.OcrText) < minChars;
    }

    /// <summary>
    /// Used at PDF import: when PdfPig text is empty or below the alphanumeric threshold, enqueue Tesseract OCR.
    /// </summary>
    public static bool PdfExtractedTextNeedsOcr(string? text, IConfigStore config)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var minChars = config.GetSettingInt(SettingPdfMinCharsForSkip, 50);
        if (minChars <= 0) return false;
        return CountAlphanumeric(text) < minChars;
    }

    private static int CountAlphanumeric(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var n = 0;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) n++;
        }
        return n;
    }
}
