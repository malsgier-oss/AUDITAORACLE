using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Simple chart rendering for reports using QuestPDF layout (no external chart library).
/// </summary>
public static class ReportCharts
{
    /// <summary>Render a simple horizontal bar chart for daily document counts.</summary>
    public static void ComposeDailyBarChart(IContainer container, List<(string Date, int Count)> data, bool includeChart = true)
    {
        if (!includeChart || data.Count == 0)
            return;

        var maxCount = data.Max(d => d.Count);
        if (maxCount == 0) maxCount = 1;

        container.PaddingTop(12).Column(column =>
        {
            column.Item().Text("Documents per Day").Bold().FontSize(11).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(6);

            // Limit to 31 bars max for readability (e.g. one month)
            var toShow = data.Count > 31 ? data.TakeLast(31).ToList() : data;

            foreach (var (date, count) in toShow)
            {
                var barWidth = Math.Max(4, (float)count / maxCount * 180);
                column.Item().PaddingBottom(4).Row(row =>
                {
                    row.ConstantItem(70).Text(date).FontSize(9);
                    row.RelativeItem().Height(14).Background(Colors.Grey.Lighten3).Padding(2)
                        .Row(barRow =>
                        {
                            barRow.ConstantItem(barWidth).Background(Colors.Blue.Medium).Height(10);
                        });
                    row.ConstantItem(40).AlignRight().Text(ArabicFormattingService.FormatNumber(count)).FontSize(9);
                });
            }
        });
    }

    /// <summary>Render a simple horizontal bar chart for branch/section counts.</summary>
    public static void ComposeSummaryBarChart(IContainer container, List<(string Name, int Count)> data, string title, bool includeChart = true)
    {
        if (!includeChart || data.Count == 0)
            return;

        var maxCount = data.Max(d => d.Count);
        if (maxCount == 0) maxCount = 1;

        container.PaddingTop(12).Column(column =>
        {
            column.Item().Text(title).Bold().FontSize(11).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(6);

            foreach (var (name, count) in data.Take(15))
            {
                var barWidth = Math.Max(4, (float)count / maxCount * 160);
                column.Item().PaddingBottom(4).Row(row =>
                {
                    row.ConstantItem(100).Text(name.Length > 20 ? name[..20] + "..." : name).FontSize(9);
                    row.RelativeItem().Height(14).Background(Colors.Grey.Lighten3).Padding(2)
                        .Row(barRow =>
                        {
                            barRow.ConstantItem(barWidth).Background(Colors.Blue.Medium).Height(10);
                        });
                    row.ConstantItem(40).AlignRight().Text(ArabicFormattingService.FormatNumber(count)).FontSize(9);
                });
            }
        });
    }
}
