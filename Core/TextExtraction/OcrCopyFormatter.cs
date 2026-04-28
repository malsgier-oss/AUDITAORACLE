using System.Text.RegularExpressions;

namespace WorkAudit.Core.TextExtraction;

/// <summary>Post-processes OCR copy text for spacing around punctuation.</summary>
public static class OcrCopyFormatter
{
    private static readonly Regex SpaceBeforeClosingPunct = new(
        @"\s+([.,;:!?%\]\)}»" + "\u060C\u061B\u061F" + @"])",
        RegexOptions.Compiled);

    private static readonly Regex SpaceAfterOpeningPunct = new(
        @"([(\[{«" + "\u2018\u201C" + @"])\s+",
        RegexOptions.Compiled);

    /// <summary>Format text copied from span range (trim + punctuation spacing).</summary>
    public static string FormatCopy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text.Trim();
        if (s.Length == 0) return "";
        s = SpaceBeforeClosingPunct.Replace(s, "$1");
        s = SpaceAfterOpeningPunct.Replace(s, "$1");
        return s;
    }
}
