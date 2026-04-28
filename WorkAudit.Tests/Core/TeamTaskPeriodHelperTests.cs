using WorkAudit.Core.TeamTasks;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Core;

public class TeamTaskPeriodHelperTests
{
    [Fact]
    public void Daily_period_key_is_calendar_date()
    {
        var d = new DateTime(2026, 4, 10);
        Assert.Equal("2026-04-10", TeamTaskPeriodHelper.GetPeriodKey(d, TeamTaskRecurrence.Daily));
    }

    [Fact]
    public void Monthly_period_key_is_year_month()
    {
        var d = new DateTime(2026, 1, 31);
        Assert.Equal("2026-01", TeamTaskPeriodHelper.GetPeriodKey(d, TeamTaskRecurrence.Monthly));
    }

    [Fact]
    public void Weekly_period_key_matches_iso_week()
    {
        var d = new DateTime(2026, 4, 6);
        Assert.Equal("2026-W15", TeamTaskPeriodHelper.GetIsoWeekPeriodKey(d));
        Assert.Equal("2026-W15", TeamTaskPeriodHelper.GetPeriodKey(d, TeamTaskRecurrence.Weekly));
    }

    [Fact]
    public void Active_window_respects_start_and_end()
    {
        var t = new DateTime(2026, 4, 10);
        Assert.True(TeamTaskPeriodHelper.IsInActiveWindow(t, "2026-04-01", "2026-04-30"));
        Assert.False(TeamTaskPeriodHelper.IsInActiveWindow(t, "2026-04-15", null));
        Assert.False(TeamTaskPeriodHelper.IsInActiveWindow(t, "2026-04-01", "2026-04-05"));
    }
}
