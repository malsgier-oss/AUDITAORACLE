using System.IO;
using System.Text.RegularExpressions;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// Canonical folder layout for stored documents. 100% local.
/// Hierarchy: Branch / Section / DocType [/ Date]
/// Non-clearing: BASE / Branch / Section / DocType / Date
/// Clearing: BASE / Branch / Clearing / Direction / Status / DocType / Date
/// </summary>
public static class Folders
{
    private static readonly Regex SanitizeRe = new(@"[<>""/\\|?*;]+", RegexOptions.Compiled);
    private static readonly Regex DateSegmentRe = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public static string SanitizeSegment(string s, int maxLen = 64)
    {
        if (string.IsNullOrEmpty(s)) return "default";
        s = SanitizeRe.Replace(s, "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > maxLen ? s[..maxLen] : (s.Length > 0 ? s : "default");
    }

    public static string GetFolderPath(string baseDir, string section, string docType, string? branch = null, string? clearingDirection = null, string? clearingStatus = null)
    {
        var branchSeg = SanitizeSegment(string.IsNullOrEmpty(branch) ? Branches.Default : branch);
        var sec = SanitizeSegment(section);
        var dt = SanitizeSegment(docType);

        if (sec.Equals("Clearing", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(clearingDirection) && !string.IsNullOrEmpty(clearingStatus))
        {
            var cd = SanitizeSegment(clearingDirection);
            var cs = SanitizeSegment(clearingStatus);
            return Path.Combine(baseDir, branchSeg, "Clearing", cd, cs, dt);
        }
        return Path.Combine(baseDir, branchSeg, sec, dt);
    }

    /// <summary>
    /// Returns the relative folder path (without base dir) for a document.
    /// Hierarchy: Branch / Section / DocType, or with date: Branch / Section / DocType / Date.
    /// </summary>
    /// <param name="documentDateYyyyMmDd">Optional date segment (yyyy-MM-dd). When provided, appended as last path segment.</param>
    public static string GetDocumentPath(string section, string docType,
        string? branch = null, string? clearingDirection = null, string? clearingStatus = null,
        string? documentDateYyyyMmDd = null)
    {
        var branchSeg = SanitizeSegment(string.IsNullOrEmpty(branch) ? Branches.Default : branch);
        var sec = SanitizeSegment(section);
        var dt = SanitizeSegment(docType);

        string relative;
        if (sec.Equals("Clearing", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(clearingDirection) && !string.IsNullOrEmpty(clearingStatus))
        {
            var cd = SanitizeSegment(clearingDirection);
            var cs = SanitizeSegment(clearingStatus);
            relative = Path.Combine(branchSeg, "Clearing", cd, cs, dt);
        }
        else
        {
            relative = Path.Combine(branchSeg, sec, dt);
        }

        if (!string.IsNullOrEmpty(documentDateYyyyMmDd) && DateSegmentRe.IsMatch(documentDateYyyyMmDd.Trim()))
            relative = Path.Combine(relative, documentDateYyyyMmDd.Trim());

        return relative;
    }
}
