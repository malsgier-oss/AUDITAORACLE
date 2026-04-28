using WorkAudit.Domain;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Pattern-based intelligence service for generating audit insights without LLM dependency.
/// Provides deterministic, bilingual output for audit compliance.
/// </summary>
public class IntelligenceService : IIntelligenceService
{
    public string GenerateExecutiveSummary(List<Document> documents, List<object> findings, string language)
    {
        var total = documents.Count;
        var cleared = documents.Count(d => d.Status == Enums.Status.Cleared);
        var issues = documents.Count(d => d.Status == Enums.Status.Issue);
        var pending = documents.Count(d => d.Status == Enums.Status.Reviewed || d.Status == Enums.Status.ReadyForAudit);
        var archived = documents.Count(d => d.Status == Enums.Status.Archived);

        var active = total - archived;
        var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
        var issueRate = total > 0 ? (decimal)issues / total * 100 : 0;

        // Determine risk posture based on thresholds
        string riskPosture;
        if (issueRate > 20)
            riskPosture = language == "ar" ? "عالي" : "High";
        else if (issueRate > 10)
            riskPosture = language == "ar" ? "متوسط" : "Moderate";
        else
            riskPosture = language == "ar" ? "منخفض" : "Low";

        if (language == "ar")
        {
            return $"تم معالجة {ArabicFormattingService.FormatNumber(total)} مستند خلال الفترة المحددة. " +
                   $"معدل التصفية {ArabicFormattingService.FormatDecimal(clearingRate)}%. " +
                   $"تم تحديد {ArabicFormattingService.FormatNumber(issues)} مشكلة تتطلب المتابعة " +
                   $"({ArabicFormattingService.FormatDecimal(issueRate)}% من الإجمالي). " +
                   $"تقييم المخاطر الإجمالي: {riskPosture}. " +
                   (pending > 0 ? $"{ArabicFormattingService.FormatNumber(pending)} مستند قيد المراجعة." : "");
        }
        else
        {
            return $"Processed {ArabicFormattingService.FormatNumber(total)} documents during the specified period. " +
                   $"Clearing rate is {ArabicFormattingService.FormatDecimal(clearingRate)}%. " +
                   $"Identified {ArabicFormattingService.FormatNumber(issues)} issues requiring follow-up " +
                   $"({ArabicFormattingService.FormatDecimal(issueRate)}% of total). " +
                   $"Overall risk posture: {riskPosture}. " +
                   (pending > 0 ? $"{ArabicFormattingService.FormatNumber(pending)} documents pending review." : "");
        }
    }

    public List<string> GenerateRecommendations(List<object> findings, string language)
    {
        var recommendations = new List<string>();
        var criticalCount = findings.Count; // Simplified - in real implementation, filter by severity

        // Pattern-based recommendation rules
        if (criticalCount > 10)
        {
            recommendations.Add(language == "ar"
                ? "تخصيص موارد إضافية لمعالجة المشكلات الحرجة المتراكمة"
                : "Allocate additional resources to address accumulated critical issues");
        }

        if (criticalCount > 5)
        {
            recommendations.Add(language == "ar"
                ? "إجراء مراجعة عاجلة لإجراءات الرقابة الداخلية"
                : "Conduct urgent review of internal control procedures");
        }

        // Always include these foundational recommendations
        recommendations.Add(language == "ar"
            ? "تحديث وتوثيق سياسات وإجراءات المراجعة"
            : "Update and document audit policies and procedures");

        recommendations.Add(language == "ar"
            ? "تعزيز برامج التدريب للموظفين على أنظمة الامتثال"
            : "Enhance employee training programs on compliance systems");

        if (criticalCount == 0)
        {
            recommendations.Add(language == "ar"
                ? "الحفاظ على مستوى الامتثال الحالي من خلال المراجعات الدورية"
                : "Maintain current compliance level through periodic reviews");
        }

        return recommendations;
    }

    public Dictionary<(int Likelihood, int Impact), int> GenerateRiskMatrix(List<object> findings)
    {
        // Risk matrix: 3x3 grid (Low/Medium/High likelihood × Low/Medium/High impact)
        var matrix = new Dictionary<(int, int), int>();

        // Initialize all cells
        for (int likelihood = 1; likelihood <= 3; likelihood++)
        {
            for (int impact = 1; impact <= 3; impact++)
            {
                matrix[(likelihood, impact)] = 0;
            }
        }

        // Simplified distribution logic
        // In real implementation, would analyze finding severity and frequency
        var totalFindings = findings.Count;

        if (totalFindings > 0)
        {
            // Example distribution pattern
            matrix[(2, 2)] = totalFindings / 2; // Medium likelihood, medium impact
            matrix[(1, 2)] = totalFindings / 4; // Low likelihood, medium impact
            matrix[(2, 1)] = totalFindings / 4; // Medium likelihood, low impact
        }

        return matrix;
    }

    public string GenerateStrategicNarrative(IReadOnlyList<Document> documents, ComparisonResult? yearOverYear, IReadOnlyList<Note> criticalNotes, string language)
    {
        var isAr = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var total = documents.Count;
        var issues = documents.Count(d => d.Status == Enums.Status.Issue);
        var top = documents.GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .OrderByDescending(g => g.Count()).FirstOrDefault();
        var topName = top?.Key ?? "-";
        var topCount = top?.Count() ?? 0;

        if (isAr)
        {
            var sb = $"رؤية استراتيجية: تمت معالجة {ArabicFormattingService.FormatNumber(total)} مستند. ";
            if (issues > 0) sb += $"يظل التركيز على {ArabicFormattingService.FormatNumber(issues)} عنصرًا بها مشكلات. ";
            if (topCount > 0) sb += $"يُقاس أعلى حمل على مستوى الفرع: {topName} ({ArabicFormattingService.FormatNumber(topCount)} مستند). ";
            if (yearOverYear != null && yearOverYear.PreviousPeriodTotal > 0)
                sb += $"مقارنة بالسنة: التغيير {yearOverYear.PercentChange:0.0}٪. ";
            if (criticalNotes.Count > 0)
                sb += $"توجد {ArabicFormattingService.FormatNumber(criticalNotes.Count)} ملاحظات حرجة/عالية تتطلب اطلاع الإدارة.";
            return sb;
        }
        {
            var sb = $"Strategic view: {ArabicFormattingService.FormatNumber(total)} documents processed in scope. ";
            if (issues > 0) sb += $"Priority remains on {ArabicFormattingService.FormatNumber(issues)} open issue items. ";
            if (topCount > 0) sb += $"Largest volume concentration: {topName} ({ArabicFormattingService.FormatNumber(topCount)} documents). ";
            if (yearOverYear != null && yearOverYear.PreviousPeriodTotal > 0)
                sb += $"Year-over-year volume change: {(yearOverYear.PercentChange >= 0 ? "+" : "")}{yearOverYear.PercentChange:0.0}%. ";
            if (criticalNotes.Count > 0)
                sb += $"{ArabicFormattingService.FormatNumber(criticalNotes.Count)} critical/high-severity notes warrant management awareness.";
            return sb;
        }
    }

    public List<StrategicInsight> IdentifyStrategicInsights(IReadOnlyList<Document> documents, IReadOnlyDictionary<string, int> branchOrSectionCounts, string language)
    {
        var isAr = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var list = new List<StrategicInsight>();
        if (documents.Count == 0) return list;

        var best = branchOrSectionCounts.OrderByDescending(x => x.Value).FirstOrDefault();
        if (!string.IsNullOrEmpty(best.Key) && best.Value > 0)
        {
            list.Add(new StrategicInsight
            {
                Category = isAr ? "الأداء" : "Performance",
                Title = isAr ? "تركيز الحجوم" : "Volume concentration",
                Description = isAr
                    ? $"على مستوى الفرع/الوحدة، يظهر {best.Key} أعلى عدد مستندات في الفترة ({best.Value})."
                    : $"Within the scope, {best.Key} shows the largest document count ({best.Value}) for the period."
            });
        }

        var issue = documents.Count(d => d.Status == Enums.Status.Issue);
        if (issue > 0)
        {
            list.Add(new StrategicInsight
            {
                Category = isAr ? "المخاطر" : "Risk",
                Title = isAr ? "مشكلات مفتوحة" : "Open issues",
                Description = isAr
                    ? $"يُنصح بمتابعة شاملة: {issue} وثيقة(ات) بها شكل مشكلة."
                    : $"Cross-functional follow-up is advised: {issue} document(s) in Issue status."
            });
        }

        return list;
    }

    public List<ExecutiveAction> GenerateExecutiveActions(IReadOnlyList<object> findings, string language)
    {
        var isAr = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var actions = new List<ExecutiveAction>();
        var n = findings.Count;
        if (n > 15)
        {
            actions.Add(new ExecutiveAction
            {
                Priority = ExecutiveActionPriority.Critical,
                Title = isAr ? "تخصيص طاقة لمعالجة الاختناقات" : "Allocate capacity to clear backlog",
                Description = isAr
                    ? "مؤشر ارتفاع الاختلالات يتجاوز عتبة 15. يوصى بالجلسة الأسبوعية مع مالكي العمل."
                    : "Exception volume is elevated. Convene a weekly triage with process owners until counts normalize.",
                SuggestedOwner = isAr ? "العمليات" : "Operations"
            });
        }
        if (n > 5)
        {
            actions.Add(new ExecutiveAction
            {
                Priority = ExecutiveActionPriority.High,
                Title = isAr ? "تعزيز الرقابة الداخلية" : "Strengthen internal control testing",
                Description = isAr
                    ? "إجراء مراجعة سريعة لنقاط التحكم لكل فرع/قسم مرتبط."
                    : "Run targeted control testing on the branches/sections with the highest error rates."
            });
        }
        if (n == 0)
        {
            actions.Add(new ExecutiveAction
            {
                Priority = ExecutiveActionPriority.Low,
                Title = isAr ? "الحفاظ على الإيقاع" : "Sustain the rhythm",
                Description = isAr
                    ? "لا مؤشرات حرجة. استمر في الارتقاء بمراجعات الضمان الجودة الربعية."
                    : "No material findings spike. Continue quarterly quality assurance walkthroughs."
            });
        }
        return actions;
    }
}
