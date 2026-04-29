using System.Globalization;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Formatting service for reports. Uses Western numerals (0-9) for clarity in banking context.
/// Supports Arabic month names for date display.
/// </summary>
public static class ArabicFormattingService
{
    /// <summary>Format number with Western numerals and thousands separator (e.g. 1,247).</summary>
    public static string FormatNumber(int number)
    {
        return number.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>Format decimal with Western numerals (e.g. 1,247.50).</summary>
    public static string FormatDecimal(decimal number, int decimals = 2)
    {
        return number.ToString($"N{decimals}", CultureInfo.InvariantCulture);
    }

    /// <summary>Format percentage (e.g. 78.5%).</summary>
    public static string FormatPercentage(decimal percentage)
    {
        return percentage.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Format date in ISO format (yyyy-MM-dd).</summary>
    public static string FormatDate(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Format date with Arabic month name (e.g. 6 فبراير 2026).</summary>
    public static string FormatDateArabic(DateTime date)
    {
        var monthName = GetArabicMonthName(date.Month);
        return $"{date.Day} {monthName} {date.Year}";
    }

    /// <summary>Get Arabic month name.</summary>
    public static string GetArabicMonthName(int month)
    {
        var monthNames = new[]
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };
        return monthNames[Math.Clamp(month - 1, 0, 11)];
    }

    /// <summary>Check if text contains Arabic characters.</summary>
    public static bool IsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Any(c => c >= 0x0600 && c <= 0x06FF);
    }
}
