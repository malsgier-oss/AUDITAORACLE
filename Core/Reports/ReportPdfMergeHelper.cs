using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;

namespace WorkAudit.Core.Reports;

/// <summary>Appends one PDF to another in-place (used for attestation page after main report is written and hashed once).</summary>
public static class ReportPdfMergeHelper
{
    public static void AppendInPlace(string mainPath, string appendixPath)
    {
        if (string.IsNullOrEmpty(mainPath) || !File.Exists(mainPath)) return;
        if (string.IsNullOrEmpty(appendixPath) || !File.Exists(appendixPath)) return;

        using var output = new PdfDocument();
        AppendAllPages(output, mainPath);
        AppendAllPages(output, appendixPath);
        var temp = mainPath + ".merging";
        output.Save(temp);
        output.Close();
        File.Delete(mainPath);
        File.Move(temp, mainPath);
    }

    private static void AppendAllPages(PdfDocument target, string path)
    {
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        for (var i = 0; i < input.PageCount; i++)
            target.AddPage(input.Pages[i]);
    }
}
