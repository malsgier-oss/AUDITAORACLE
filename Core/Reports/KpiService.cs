using Newtonsoft.Json;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// KPI targets and variance analysis. Targets stored in app_settings as JSON.
/// </summary>
public interface IKpiService
{
    IReadOnlyList<KpiTarget> GetTargets();
    void SaveTargets(IEnumerable<KpiTarget> targets);
    KpiTarget? GetTarget(string kpiName, string? branch = null, string? section = null);
    KpiVarianceResult GetVariance(string kpiName, decimal actual, string? branch = null, string? section = null);
}

/// <summary>Result of KPI variance analysis.</summary>
public class KpiVarianceResult
{
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePercent { get; set; }
    /// <summary>OnTarget, Warning, Critical.</summary>
    public string Status { get; set; } = "";
}

public class KpiService : IKpiService
{
    private const string Key = "kpi_targets_json";
    private readonly IConfigStore _configStore;

    public KpiService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public IReadOnlyList<KpiTarget> GetTargets()
    {
        var json = _configStore.GetSettingValue(Key);
        if (string.IsNullOrWhiteSpace(json)) return GetDefaultTargets();
        try
        {
            var list = JsonConvert.DeserializeObject<List<KpiTarget>>(json);
            return list ?? GetDefaultTargets();
        }
        catch
        {
            return GetDefaultTargets();
        }
    }

    public void SaveTargets(IEnumerable<KpiTarget> targets)
    {
        var json = JsonConvert.SerializeObject(targets.ToList());
        _configStore.SetSetting(Key, json);
    }

    public KpiTarget? GetTarget(string kpiName, string? branch = null, string? section = null)
    {
        var targets = GetTargets();
        var match = targets
            .Where(t => t.KpiName == kpiName)
            .Where(t => string.IsNullOrEmpty(t.Branch) || t.Branch == branch)
            .Where(t => string.IsNullOrEmpty(t.Section) || t.Section == section)
            .OrderByDescending(t => t.Branch != null ? 1 : 0)
            .ThenByDescending(t => t.Section != null ? 1 : 0)
            .FirstOrDefault();
        return match;
    }

    public KpiVarianceResult GetVariance(string kpiName, decimal actual, string? branch = null, string? section = null)
    {
        var target = GetTarget(kpiName, branch, section);
        var result = new KpiVarianceResult { Actual = actual };
        if (target == null)
        {
            result.Target = 0;
            result.Variance = actual;
            result.VariancePercent = 0;
            result.Status = "NoTarget";
            return result;
        }
        result.Target = target.Target;
        result.Variance = actual - target.Target;
        result.VariancePercent = target.Target != 0 ? (actual - target.Target) / target.Target * 100 : 0;

        var isHigherBetter = kpiName is KpiNames.ClearingRate or KpiNames.DocumentsProcessed or KpiNames.Throughput;
        var isLowerBetter = kpiName == KpiNames.IssueRate;

        if (isHigherBetter)
            result.Status = actual >= target.Target ? "OnTarget" : actual >= target.Warning ? "Warning" : "Critical";
        else if (isLowerBetter)
            result.Status = actual <= target.Target ? "OnTarget" : actual <= target.Warning ? "Warning" : "Critical";
        else
            result.Status = "OnTarget";

        return result;
    }

    private static List<KpiTarget> GetDefaultTargets()
    {
        return GetDefaultTargetsStatic();
    }

    /// <summary>Returns default KPI targets. Used by admin UI to reset.</summary>
    public static List<KpiTarget> GetDefaultTargetsStatic()
    {
        return new List<KpiTarget>
        {
            new() { KpiName = KpiNames.ClearingRate, Target = 80, Warning = 70, Critical = 60, Period = "Monthly" },
            new() { KpiName = KpiNames.Throughput, Target = 50, Warning = 40, Critical = 30, Period = "Monthly" },
            new() { KpiName = KpiNames.IssueRate, Target = 5, Warning = 8, Critical = 10, Period = "Monthly" }
        };
    }
}
