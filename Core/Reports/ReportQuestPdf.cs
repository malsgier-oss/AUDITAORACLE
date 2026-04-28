using System.Threading;
using QuestPDF.Infrastructure;

namespace WorkAudit.Core.Reports;

/// <summary>Central QuestPDF license and optional layout diagnostics. Call <see cref="Configure"/> before generating any report PDF.</summary>
public static class ReportQuestPdf
{
    private static int _configured;

    public static void Configure()
    {
        if (Interlocked.CompareExchange(ref _configured, 1, 0) != 0) return;
        QuestPDF.Settings.License = LicenseType.Community;
#if DEBUG
        QuestPDF.Settings.EnableDebugging = true;
#endif
    }
}
