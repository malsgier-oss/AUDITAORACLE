using System.Text.RegularExpressions;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Reduces OCR damage to numeric and ID-like tokens (decimals, thousands separators, hyphenated codes).
/// Does not strip punctuation from non-numeric text.
/// </summary>
public static class OcrNumberProtector
{
    /// <summary>Latin letter code + hyphen broken by spaces + digits (e.g. INV - 12345).</summary>
    private static readonly Regex IdHyphenBreak = new(
        @"(?<=[A-Za-z]{2,})[\s\u00A0]+[-–—][\s\u00A0]*(?=\d)",
        RegexOptions.Compiled);

    /// <summary>Digit, whitespace, separator, whitespace, digit — collapse inner spaces.</summary>
    private static readonly Regex DigitSepDigit = new(
        @"(?<=\d)[\s\u00A0]+([.,\u066B\u066C])[\s\u00A0]+(?=\d)",
        RegexOptions.Compiled);

    /// <summary>Thin spaces between digits inside a number chunk.</summary>
    private static readonly Regex DigitSpaceDigit = new(
        @"(?<=\d)[\s\u00A0]{1,2}(?=\d)",
        RegexOptions.Compiled);

    /// <summary>Run after merge / <see cref="OcrTextNormalizer"/> to repair common splits.</summary>
    public static string Repair(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = RepairLine(lines[i]);

        return string.Join("\n", lines);
    }

    private static string RepairLine(string line)
    {
        var s = IdHyphenBreak.Replace(line, "-");
        s = DigitSepDigit.Replace(s, "$1");
        s = DigitSpaceDigit.Replace(s, "");
        return s;
    }
}
