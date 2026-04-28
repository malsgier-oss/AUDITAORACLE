using System;
using System.Threading;
using System.Threading.Tasks;
using WorkAudit.Domain;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// OCR service wrapper that delegates to Tesseract.
/// </summary>
public sealed class SelectingOcrService : IOcrService, IDisposable
{
    public const string EngineTesseract = "tesseract";
    private readonly TesseractOcrService _tesseract;

    public SelectingOcrService(TesseractOcrService tesseract)
    {
        _tesseract = tesseract ?? throw new ArgumentNullException(nameof(tesseract));

        _tesseract.OcrCompleted += OnInnerOcrCompleted;
    }

    private void OnInnerOcrCompleted(Document doc) => OcrCompleted?.Invoke(doc);

    public event Action<Document>? OcrCompleted;

    public void EnqueueOcrTask(Document document)
    {
        _tesseract.EnqueueOcrTask(document);
    }

    public Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default)
    {
        return _tesseract.ExtractTextAsync(imagePath, ct);
    }

    public void Dispose()
    {
        _tesseract.OcrCompleted -= OnInnerOcrCompleted;
        _tesseract.Dispose();
    }
}
