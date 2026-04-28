namespace WorkAudit.Core.Helpers;

/// <summary>
/// UTC+2 time zone helper for audit log date ranges and display.
/// All audit times are shown and interpreted in UTC+2.
/// </summary>
public static class AuditTimeHelper
{
    private static readonly TimeSpan UtcPlus2 = TimeSpan.FromHours(2);

    /// <summary>
    /// Converts a date-only (user-selected "From" in UTC+2) to start of that day in UTC for querying.
    /// </summary>
    public static DateTime? ToUtcFromDateUtcPlus2(DateTime dateOnly)
    {
        var d = dateOnly.Date;
        var startUtcPlus2 = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(-2);
        return startUtcPlus2;
    }

    /// <summary>
    /// Converts a date-only (user-selected "To" in UTC+2) to end of that day in UTC for querying.
    /// </summary>
    public static DateTime? ToUtcToDateUtcPlus2(DateTime dateOnly)
    {
        var d = dateOnly.Date;
        var endUtcPlus2 = new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, 999, DateTimeKind.Utc).AddHours(-2);
        return endUtcPlus2;
    }

    /// <summary>
    /// Formats a stored UTC timestamp string for display in UTC+2.
    /// </summary>
    public static string FormatForDisplay(string? utcTimestamp)
    {
        if (string.IsNullOrEmpty(utcTimestamp)) return "";
        if (!DateTime.TryParse(utcTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return utcTimestamp;
        var utc2 = dt.Add(UtcPlus2);
        return utc2.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
