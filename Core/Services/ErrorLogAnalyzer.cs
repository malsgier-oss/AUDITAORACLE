using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WorkAudit.Domain;

namespace WorkAudit.Core.Services;

public sealed class LogFilter
{
    public DateTime? SinceUtc { get; set; }
    public DateTime? UntilUtc { get; set; }
    public string? MinLevel { get; set; }
    public string? ComponentContains { get; set; }
    public string? MessageContains { get; set; }
    public int MaxLines { get; set; } = 5000;
}

/// <summary>Parses WorkAudit Serilog file output and performance log lines.</summary>
public interface IErrorLogAnalyzer
{
    IReadOnlyList<LogEntryModel> ParseMainLogs(string logDirectory, DateTime sinceUtc, int maxFilesToScan = 14);
    IReadOnlyList<LogEntryModel> FilterLogs(IEnumerable<LogEntryModel> entries, LogFilter filter);
    Dictionary<string, int> GetErrorCategoryCounts(IEnumerable<LogEntryModel> entries, DateTime sinceUtc);
    IReadOnlyList<PerformanceMetricModel> ParsePerformanceLogs(string logDirectory, DateTime sinceUtc, int maxLines = 3000);
    IReadOnlyList<ErrorTrendPoint> BuildHourlyTrend(IEnumerable<LogEntryModel> entries, DateTime sinceUtc);
}

public sealed class ErrorLogAnalyzer : IErrorLogAnalyzer
{
    // Example: 2025-05-04 15:30:00.123 +02:00 [ERR] [PC/12345] WorkAudit.Storage.DocumentStore - message
    private static readonly Regex LineStartRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}\s+[+-]\d{2}:\d{2})\s+\[(?<lvl>[A-Z]{3})\]\s+\[(?<mach>[^/\]]+)/(?<pid>\d+)\]\s+(?<src>[^\s].*?)\s+-\s+(?<msg>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PerfRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})\s+PERF:\s+(?<rest>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<LogEntryModel> ParseMainLogs(string logDirectory, DateTime sinceUtc, int maxFilesToScan = 14)
    {
        if (!Directory.Exists(logDirectory))
            return Array.Empty<LogEntryModel>();

        var files = Directory.GetFiles(logDirectory, "workaudit-*.log")
            .OrderByDescending(f => f)
            .Take(maxFilesToScan)
            .ToList();

        var results = new List<LogEntryModel>();
        foreach (var path in files)
        {
            try
            {
                ParseLogFile(path, sinceUtc, results);
            }
            catch
            {
                // locked file or permission — skip
            }
        }

        return results.OrderByDescending(e => e.TimestampUtc).ToList();
    }

    private static void ParseLogFile(string path, DateTime sinceUtc, List<LogEntryModel> sink)
    {
        LogEntryModel? current = null;
        var sb = new StringBuilder();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine;
            var m = LineStartRegex.Match(line);
            if (m.Success)
            {
                FlushCurrent(sink, current, sb);
                sb.Clear();

                if (!TryParseTimestamp(m.Groups["ts"].Value, out var ts) || ts < sinceUtc)
                {
                    current = null;
                    continue;
                }

                int.TryParse(m.Groups["pid"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid);
                current = new LogEntryModel
                {
                    TimestampUtc = ts,
                    Level = m.Groups["lvl"].Value,
                    MachineName = m.Groups["mach"].Value,
                    ProcessId = pid,
                    Component = m.Groups["src"].Value.Trim(),
                    Message = m.Groups["msg"].Value
                };
            }
            else if (current != null)
            {
                sb.AppendLine(line);
            }
        }

        FlushCurrent(sink, current, sb);
    }

    private static void FlushCurrent(List<LogEntryModel> sink, LogEntryModel? current, StringBuilder sb)
    {
        if (current == null) return;
        var extra = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(extra))
            current.ExceptionBlock = extra;
        sink.Add(current);
    }

    private static bool TryParseTimestamp(string raw, out DateTime utc)
    {
        utc = default;
        // Serilog outputs offset like +02:00 in invariant culture
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            utc = dt.ToUniversalTime();
            return true;
        }

        return false;
    }

    public IReadOnlyList<LogEntryModel> FilterLogs(IEnumerable<LogEntryModel> entries, LogFilter filter)
    {
        var q = entries.AsEnumerable();
        if (filter.SinceUtc.HasValue)
            q = q.Where(e => e.TimestampUtc >= filter.SinceUtc.Value);
        if (filter.UntilUtc.HasValue)
            q = q.Where(e => e.TimestampUtc <= filter.UntilUtc.Value);
        if (!string.IsNullOrEmpty(filter.MinLevel))
        {
            var min = LevelOrder(filter.MinLevel);
            q = q.Where(e => LevelOrder(e.Level) >= min);
        }

        if (!string.IsNullOrWhiteSpace(filter.ComponentContains))
        {
            var c = filter.ComponentContains.Trim();
            q = q.Where(e => e.Component.Contains(c, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.MessageContains))
        {
            var c = filter.MessageContains.Trim();
            q = q.Where(e => e.Message.Contains(c, StringComparison.OrdinalIgnoreCase)
                             || (e.ExceptionBlock?.Contains(c, StringComparison.OrdinalIgnoreCase) == true));
        }

        return q.Take(filter.MaxLines).ToList();
    }

    private static int LevelOrder(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "VRB" or "DBG" => 0,
            "INF" => 1,
            "WRN" => 2,
            "ERR" => 3,
            "FTL" => 4,
            _ => 1
        };
    }

    public Dictionary<string, int> GetErrorCategoryCounts(IEnumerable<LogEntryModel> entries, DateTime sinceUtc)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.TimestampUtc < sinceUtc) continue;
            if (e.Level is not ("ERR" or "FTL")) continue;
            var key = string.IsNullOrWhiteSpace(e.Component) ? "(unknown)" : e.Component;
            dict[key] = dict.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        return dict;
    }

    public IReadOnlyList<PerformanceMetricModel> ParsePerformanceLogs(string logDirectory, DateTime sinceUtc, int maxLines = 3000)
    {
        if (!Directory.Exists(logDirectory))
            return Array.Empty<PerformanceMetricModel>();

        var files = Directory.GetFiles(logDirectory, "performance-*.log")
            .OrderByDescending(f => f)
            .Take(7)
            .ToList();

        var list = new List<PerformanceMetricModel>();
        foreach (var path in files)
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var m = PerfRegex.Match(rawLine);
                if (!m.Success) continue;
                if (!DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                    continue;
                if (ts < sinceUtc) continue;

                var rest = m.Groups["rest"].Value;
                var metric = ParsePerfRest(ts.ToUniversalTime(), rest);
                if (metric != null)
                    list.Add(metric);
                if (list.Count >= maxLines)
                    return list;
            }
        }

        return list.OrderByDescending(x => x.TimestampUtc).ToList();
    }

    private static PerformanceMetricModel? ParsePerfRest(DateTime tsUtc, string rest)
    {
        // "Import completed in 120ms (5 items)" style from LoggingService.LogPerformanceMetric
        long ms = 0;
        var lower = rest;
        var idx = lower.IndexOf(" completed in ", StringComparison.OrdinalIgnoreCase);
        string op = rest;
        if (idx >= 0)
        {
            op = rest[..idx].Trim();
            var after = rest[(idx + " completed in ".Length)..];
            var msIdx = after.IndexOf("ms", StringComparison.OrdinalIgnoreCase);
            if (msIdx > 0)
            {
                var num = after[..msIdx].Trim();
                long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            }
        }
        else if (rest.Contains("ms", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(rest, @"(\d+)\s*ms");
            if (match.Success)
                long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
        }

        return new PerformanceMetricModel
        {
            TimestampUtc = tsUtc,
            Operation = string.IsNullOrEmpty(op) ? rest : op,
            DurationMs = ms,
            IsSlow = ms >= 1000
        };
    }

    public IReadOnlyList<ErrorTrendPoint> BuildHourlyTrend(IEnumerable<LogEntryModel> entries, DateTime sinceUtc)
    {
        var grouped = entries
            .Where(e => e.TimestampUtc >= sinceUtc)
            .GroupBy(e => new DateTime(e.TimestampUtc.Year, e.TimestampUtc.Month, e.TimestampUtc.Day, e.TimestampUtc.Hour, 0, 0, DateTimeKind.Utc));

        var list = new List<ErrorTrendPoint>();
        foreach (var g in grouped.OrderBy(x => x.Key))
        {
            list.Add(new ErrorTrendPoint
            {
                HourUtc = g.Key,
                ErrorCount = g.Count(e => e.Level is "ERR" or "FTL"),
                WarningCount = g.Count(e => e.Level == "WRN")
            });
        }

        return list;
    }
}
