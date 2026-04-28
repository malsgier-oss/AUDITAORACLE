using System;
using System.Threading;
using System.Threading.Tasks;
using WorkAudit.Domain;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Interface for Optical Character Recognition services.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Extracts text from an image file.
    /// </summary>
    /// <param name="imagePath">Absolute path to the image file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted text.</returns>
    Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a document for background OCR processing.
    /// </summary>
    void EnqueueOcrTask(Document document);

    /// <summary>
    /// Event raised when a background OCR task completes.
    /// </summary>
    event Action<Document>? OcrCompleted;
}
