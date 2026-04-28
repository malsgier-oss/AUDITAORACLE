using FluentAssertions;
using WorkAudit.ViewModels;
using Xunit;

namespace WorkAudit.Tests;

public class DashboardViewModelTests
{
    private static readonly DateTime FixedNow = new(2026, 4, 17, 14, 30, 0, DateTimeKind.Local);

    [Fact]
    public void GetDateRange_Today_IsSingleCalendarDay()
    {
        var (start, end) = DashboardViewModel.GetDateRange("Today", FixedNow);
        start.Should().Be(new DateTime(2026, 4, 17));
        end.Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void GetDateRange_ThisMonth_IsFirstThroughLastDayOfMonth()
    {
        var (start, end) = DashboardViewModel.GetDateRange("This Month", FixedNow);
        start.Should().Be(new DateTime(2026, 4, 1));
        end.Should().Be(new DateTime(2026, 4, 30));
    }

    [Fact]
    public void GetDateRange_LastThreeMonths_RollingFromSameDayOfMonth()
    {
        var (start, end) = DashboardViewModel.GetDateRange("Last Three Months", FixedNow);
        start.Should().Be(new DateTime(2026, 1, 17));
        end.Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void GetDateRange_LastSixMonths_Rolling()
    {
        var (start, end) = DashboardViewModel.GetDateRange("Last Six Months", FixedNow);
        start.Should().Be(new DateTime(2025, 10, 17));
        end.Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void GetDateRange_LastNineMonths_Rolling()
    {
        var (start, end) = DashboardViewModel.GetDateRange("Last Nine Months", FixedNow);
        start.Should().Be(new DateTime(2025, 7, 17));
        end.Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void GetDateRange_Last1Year_Rolling()
    {
        var (start, end) = DashboardViewModel.GetDateRange("Last 1 Year", FixedNow);
        start.Should().Be(new DateTime(2025, 4, 17));
        end.Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void GetDateRange_AllTime_IsNullBounds()
    {
        var (start, end) = DashboardViewModel.GetDateRange("All Time", FixedNow);
        start.Should().BeNull();
        end.Should().BeNull();
    }

    [Fact]
    public void TimeRangeOptions_ContainsAllKeysUsedByGetDateRange()
    {
        foreach (var label in DashboardViewModel.TimeRangeOptions)
        {
            var (a, b) = DashboardViewModel.GetDateRange(label, FixedNow);
            if (label == "All Time")
            {
                a.Should().BeNull();
                b.Should().BeNull();
            }
            else
            {
                (a.HasValue && b.HasValue).Should().BeTrue($"{label} should return bounded dates or be All Time");
            }
        }
    }
}
