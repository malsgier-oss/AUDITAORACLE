using System.Text;

namespace WorkAudit.Core.TextExtraction;

public enum OcrLineScript
{
    Arabic,
    English,
    Mixed
}

public static class OcrBilingualOrganizer
{
    public static string Organize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var arabic = new List<string>();
        var english = new List<string>();
        var mixed = new List<string>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            switch (ClassifyLine(line))
            {
                case OcrLineScript.Arabic:
                    arabic.Add(line);
                    break;
                case OcrLineScript.English:
                    english.Add(line);
                    break;
                default:
                    mixed.Add(line);
                    break;
            }
        }

        var sb = new StringBuilder(text.Length + 64);
        AppendSection(sb, "Arabic", arabic);
        AppendSection(sb, "English", english);
        AppendSection(sb, "Mixed/Numbers", mixed);
        return sb.ToString().Trim();
    }

    public static OcrLineScript ClassifyLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return OcrLineScript.Mixed;

        var arabic = 0;
        var latin = 0;
        var digits = 0;

        foreach (var c in line)
        {
            if (IsArabic(c)) arabic++;
            else if (IsLatin(c)) latin++;
            else if (char.IsDigit(c)) digits++;
        }

        if (arabic == 0 && latin == 0)
            return OcrLineScript.Mixed;

        if (arabic >= latin * 2 && arabic > 0)
            return OcrLineScript.Arabic;

        if (latin >= arabic * 2 && latin > 0 && digits <= latin)
            return OcrLineScript.English;

        return OcrLineScript.Mixed;
    }

    private static void AppendSection(StringBuilder sb, string title, List<string> lines)
    {
        if (sb.Length > 0) sb.AppendLine().AppendLine();
        sb.Append(title).Append(':').AppendLine();
        if (lines.Count == 0)
        {
            sb.AppendLine("-");
            return;
        }

        foreach (var line in lines)
            sb.AppendLine(line);
    }

    private static bool IsArabic(char c) =>
        (c >= '\u0600' && c <= '\u06FF')
        || (c >= '\u0750' && c <= '\u077F')
        || (c >= '\u08A0' && c <= '\u08FF')
        || (c >= '\uFB50' && c <= '\uFDFF')
        || (c >= '\uFE70' && c <= '\uFEFF');

    private static bool IsLatin(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
}
