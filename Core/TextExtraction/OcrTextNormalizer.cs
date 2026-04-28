using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Cleans and normalizes raw Tesseract output for storage and display (fully local; no I/O).
/// </summary>
public static class OcrTextNormalizer
{
    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MultiNewlineRegex = new(@"\n{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HyphenLineBreakRegex = new(@"-\n(?=\p{L})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsOcrErrorMessage(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.StartsWith("OCR Error:", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Error: Tesseract", StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? text, bool mergeHyphenLineBreaks = true)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (IsOcrErrorMessage(text)) return text.TrimEnd();

        var s = text.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\n' || c == '\t')
            {
                sb.Append(c);
                continue;
            }

            if (c == '\r') continue;

            if (char.IsControl(c)) continue;

            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;

            sb.Append(c);
        }

        s = sb.ToString().Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = s.Split('\n');
        var parts = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var t = line.TrimEnd();
            t = MultiSpaceRegex.Replace(t, " ");
            parts.Add(t);
        }

        s = string.Join("\n", parts);

        if (mergeHyphenLineBreaks)
            s = HyphenLineBreakRegex.Replace(s, string.Empty);

        s = MultiNewlineRegex.Replace(s, "\n\n");
        return s.Trim();
    }
}
