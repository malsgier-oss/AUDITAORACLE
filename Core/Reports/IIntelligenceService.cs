using WorkAudit.Domain;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Intelligence service for generating insights, summaries, and recommendations.
/// Uses pattern-based analysis (no LLM required) for audit intelligence.
/// </summary>
public interface IIntelligenceService
{
    /// <summary>
    /// Generates an executive summary paragraph synthesizing audit scope, key findings, and risk posture.
    /// </summary>
    /// <param name="documents">Documents in the audit period</param>
    /// <param name="findings">Key findings and issues</param>
    /// <param name="language">Target language ("en" or "ar")</param>
    /// <returns>Executive summary text in the specified language</returns>
    string GenerateExecutiveSummary(List<Document> documents, List<object> findings, string language);

    /// <summary>
    /// Generates smart recommendations based on pattern analysis of findings.
    /// </summary>
    /// <param name="findings">Key findings and issues</param>
    /// <param name="language">Target language ("en" or "ar")</param>
    /// <returns>List of recommendation strings in the specified language</returns>
    List<string> GenerateRecommendations(List<object> findings, string language);

    /// <summary>
    /// Generates risk assessment matrix mapping likelihood and impact to finding counts.
    /// </summary>
    /// <param name="findings">Key findings to assess</param>
    /// <returns>Dictionary mapping (likelihood, impact) tuples to counts</returns>
    Dictionary<(int Likelihood, int Impact), int> GenerateRiskMatrix(List<object> findings);

    /// <summary>Strategic narrative combining document metrics and optional period comparison (executive report).</summary>
    string GenerateStrategicNarrative(IReadOnlyList<Document> documents, ComparisonResult? yearOverYear, IReadOnlyList<Note> criticalNotes, string language);

    /// <summary>Top strategic insights (best branch, issue concentration, etc.).</summary>
    List<StrategicInsight> IdentifyStrategicInsights(IReadOnlyList<Document> documents, IReadOnlyDictionary<string, int> branchOrSectionCounts, string language);

    /// <summary>Prioritized action items for leadership.</summary>
    List<ExecutiveAction> GenerateExecutiveActions(IReadOnlyList<object> findings, string language);
}
