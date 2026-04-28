using System.Globalization;
using WorkAudit.Domain;

namespace WorkAudit.Core.TeamTasks;

/// <summary>
/// Computes stable period keys for recurring team tasks using the user's local calendar.
/// Weekly uses ISO 8601 week (Monday-based), e.g. 2026-W15.
/// </summary>
public static class TeamTaskPeriodHelper
{
    public static string GetPeriodKey(DateTime localDate, string recurrence)
    {
        return recurrence switch
        {
            TeamTaskRecurrence.Daily => localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TeamTaskRecurrence.Weekly => GetIsoWeekPeriodKey(localDate),
            TeamTaskRecurrence.Monthly => localDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Format: yyyy-Www (ISO week number, 2 digits).</summary>
    public static string GetIsoWeekPeriodKey(DateTime localDate)
    {
        var year = ISOWeek.GetYear(localDate);
        var week = ISOWeek.GetWeekOfYear(localDate);
        return $"{year}-W{week:D2}";
    }

    /// <summary>Whether <paramref name="today"/> falls in the active window for the task.</summary>
    public static bool IsInActiveWindow(DateTime todayLocal, string startDateYyyyMmDd, string? endDateYyyyMmDd)
    {
        var todayStr = todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.Compare(todayStr, startDateYyyyMmDd, StringComparison.Ordinal) < 0)
            return false;
        if (!string.IsNullOrEmpty(endDateYyyyMmDd) &&
            string.Compare(todayStr, endDateYyyyMmDd, StringComparison.Ordinal) > 0)
            return false;
        return true;
    }
}
