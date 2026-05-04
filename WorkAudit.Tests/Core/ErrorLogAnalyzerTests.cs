using FluentAssertions;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Core;

public class ErrorLogAnalyzerTests
{
    [Fact]
    public void ParseMainLogs_ParsesSerilogLineAndExceptionBlock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wa_diag_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var logPath = Path.Combine(dir, "workaudit-20250101.log");
            var line1 = "2025-01-15 10:00:00.000 +00:00 [ERR] [PC/42] WorkAudit.Test.Service - Something failed";
            var line2 = "System.InvalidOperationException: bad";
            File.WriteAllText(logPath, line1 + Environment.NewLine + line2 + Environment.NewLine);

            var analyzer = new ErrorLogAnalyzer();
            var since = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entries = analyzer.ParseMainLogs(dir, since);

            entries.Should().ContainSingle();
            var e = entries[0];
            e.Level.Should().Be("ERR");
            e.Component.Should().Be("WorkAudit.Test.Service");
            e.Message.Should().Be("Something failed");
            e.ProcessId.Should().Be(42);
            e.ExceptionBlock.Should().NotBeNull();
            e.ExceptionBlock.Should().Contain("InvalidOperationException");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FilterLogs_RespectsMinLevel()
    {
        var analyzer = new ErrorLogAnalyzer();
        var entries = new[]
        {
            new LogEntryModel
            {
                TimestampUtc = DateTime.UtcNow,
                Level = "WRN",
                Component = "A",
                Message = "w"
            },
            new LogEntryModel
            {
                TimestampUtc = DateTime.UtcNow,
                Level = "ERR",
                Component = "B",
                Message = "e"
            }
        };
        var filter = new LogFilter { MinLevel = "ERR", SinceUtc = DateTime.UtcNow.AddHours(-1) };
        var filtered = analyzer.FilterLogs(entries, filter);
        filtered.Should().ContainSingle();
        filtered[0].Level.Should().Be("ERR");
    }

    [Fact]
    public void FilterLogs_RespectsComponentContains()
    {
        var analyzer = new ErrorLogAnalyzer();
        var entries = new[]
        {
            new LogEntryModel { TimestampUtc = DateTime.UtcNow, Level = "ERR", Component = "WorkAudit.Storage.A", Message = "x" },
            new LogEntryModel { TimestampUtc = DateTime.UtcNow, Level = "ERR", Component = "WorkAudit.Core.B", Message = "y" }
        };
        var filter = new LogFilter
        {
            SinceUtc = DateTime.UtcNow.AddHours(-1),
            ComponentContains = "Storage"
        };
        var filtered = analyzer.FilterLogs(entries, filter);
        filtered.Should().ContainSingle();
        filtered[0].Component.Should().Contain("Storage");
    }
}
