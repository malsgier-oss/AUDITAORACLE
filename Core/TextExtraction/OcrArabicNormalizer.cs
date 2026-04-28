using System;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Light Arabic OCR fixes: common split-letter joins (no aggressive reshaping).
/// </summary>
public static class OcrArabicNormalizer
{
    private static readonly Regex AlefLamSpace = new(@"\u0627\s+\u0644", RegexOptions.Compiled);
    private static readonly Regex LamAlefSpace = new(@"\u0644\s+\u0627", RegexOptions.Compiled);

    /// <summary>Applies ligature fixes only when the line is primarily Arabic script.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            sb.Append(IsPrimarilyArabicLine(line) ? FixLine(line) : line);
        }

        return sb.ToString();
    }

    private static bool IsArabicLetter(char c) =>
        c is >= '\u0600' and <= '\u06FF'
        or >= '\u0750' and <= '\u077F'
        or >= '\u08A0' and <= '\u08FF'
        or >= '\uFB50' and <= '\uFDFF'
        or >= '\uFE70' and <= '\uFEFF';

    private static bool IsPrimarilyArabicLine(string line)
    {
        var letters = 0;
        var arabic = 0;
        foreach (var c in line)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (IsArabicLetter(c)) arabic++;
        }

        return letters > 0 && arabic * 2 >= letters;
    }

    private static string FixLine(string line)
    {
        var s = AlefLamSpace.Replace(line, "\u0627\u0644");
        s = LamAlefSpace.Replace(s, "\u0644\u0627");
        return s;
    }
}
