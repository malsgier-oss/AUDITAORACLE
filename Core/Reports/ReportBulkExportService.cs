using System.IO;
using System.IO.Compression;
using WorkAudit.Domain;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Service for bulk export operations on report history.
/// </summary>
public interface IReportBulkExportService
{
    /// <summary>Create a ZIP archive of multiple reports.</summary>
    string ExportToZip(List<ReportHistory> reports, string zipFileName);
    
    /// <summary>Copy multiple reports to a target directory.</summary>
    int CopyReportsToDirectory(List<ReportHistory> reports, string targetDirectory);
    
    /// <summary>Generate a CSV index of report metadata.</summary>
    string GenerateMetadataCsv(List<ReportHistory> reports, string outputPath);
}

public class ReportBulkExportService : IReportBulkExportService
{
    private readonly string _defaultExportFolder;

    public ReportBulkExportService()
    {
        _defaultExportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WorkAudit",
            "Reports",
            "BulkExports");
        
        Directory.CreateDirectory(_defaultExportFolder);
    }

    public string ExportToZip(List<ReportHistory> reports, string zipFileName)
    {
        var zipPath = Path.Combine(_defaultExportFolder, zipFileName);
        if (!zipFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            zipPath += ".zip";

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        
        foreach (var report in reports)
        {
            if (!File.Exists(report.FilePath)) continue;

            var entryName = $"{report.ReportType}_{Path.GetFileName(report.FilePath)}";
            archive.CreateEntryFromFile(report.FilePath, entryName, CompressionLevel.Optimal);
        }

        var metadataCsv = GenerateMetadataCsvContent(reports);
        var metadataEntry = archive.CreateEntry("_metadata.csv");
        using (var writer = new StreamWriter(metadataEntry.Open()))
        {
            writer.Write(metadataCsv);
        }

        return zipPath;
    }

    public int CopyReportsToDirectory(List<ReportHistory> reports, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        
        var copied = 0;
        foreach (var report in reports)
        {
            if (!File.Exists(report.FilePath)) continue;

            var targetFileName = $"{report.ReportType}_{Path.GetFileName(report.FilePath)}";
            var targetPath = Path.Combine(targetDirectory, targetFileName);
            
            File.Copy(report.FilePath, targetPath, overwrite: true);
            copied++;
        }

        var metadataPath = Path.Combine(targetDirectory, "_metadata.csv");
        File.WriteAllText(metadataPath, GenerateMetadataCsvContent(reports));

        return copied;
    }

    public string GenerateMetadataCsv(List<ReportHistory> reports, string outputPath)
    {
        var content = GenerateMetadataCsvContent(reports);
        File.WriteAllText(outputPath, content);
        return outputPath;
    }

    private string GenerateMetadataCsvContent(List<ReportHistory> reports)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("UUID,Report Type,Username,Generated At,File Path,Tags,Purpose,Description,Version,App Version");

        foreach (var report in reports)
        {
            csv.AppendLine($"\"{report.Uuid}\"," +
                          $"\"{EscapeCsv(report.ReportType)}\"," +
                          $"\"{EscapeCsv(report.Username)}\"," +
                          $"\"{EscapeCsv(report.GeneratedAt)}\"," +
                          $"\"{EscapeCsv(report.FilePath)}\"," +
                          $"\"{EscapeCsv(report.Tags ?? "")}\"," +
                          $"\"{EscapeCsv(report.Purpose ?? "")}\"," +
                          $"\"{EscapeCsv(report.Description ?? "")}\"," +
                          $"\"{report.Version?.ToString() ?? ""}\"," +
                          $"\"{EscapeCsv(report.AppVersion ?? "")}\"");
        }

        return csv.ToString();
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\"", "\"\"");
    }
}
