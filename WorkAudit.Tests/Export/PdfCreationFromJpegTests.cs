using OpenCvSharp;
using WorkAudit.Core.Export;
using Xunit;

namespace WorkAudit.Tests.Export;

/// <summary>Webcam pipeline now saves page images as JPEG; PDF creation must accept them.</summary>
public class PdfCreationFromJpegTests
{
    [Fact]
    public void CreateFromImages_embeds_jpeg_page()
    {
        var dir = Path.Combine(Path.GetTempPath(), "WorkAudit_pdf_jpeg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var jpg = Path.Combine(dir, "page_001.jpg");
            using (var m = new Mat(200, 150, MatType.CV_8UC3, Scalar.Blue))
                Cv2.ImWrite(jpg, m, new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));

            var pdf = Path.Combine(dir, "out.pdf");
            PdfCreationService.CreateFromImages(new[] { jpg }, pdf);

            Assert.True(File.Exists(pdf));
            Assert.True(new FileInfo(pdf).Length > 100);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
