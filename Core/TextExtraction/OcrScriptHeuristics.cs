namespace WorkAudit.Core.TextExtraction;

/// <summary>Lightweight script detection for line reading order (e.g. RTL sort).</summary>
public static class OcrScriptHeuristics
{
    /// <summary>True when the string is mostly Arabic script letters.</summary>
    public static bool IsPrimarilyArabicScript(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var letters = 0;
        var arabic = 0;
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c is >= '\u0600' and <= '\u06FF'
                or >= '\u0750' and <= '\u077F'
                or >= '\u08A0' and <= '\u08FF'
                or >= '\uFB50' and <= '\uFDFF'
                or >= '\uFE70' and <= '\uFEFF')
                arabic++;
        }

        return letters > 0 && arabic * 2 >= letters;
    }
}
