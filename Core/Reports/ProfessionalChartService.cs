using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Professional chart rendering service with Arabic label support and corporate styling.
/// Wraps ReportCharts with enhanced formatting, RTL support, and bilingual capabilities.
/// </summary>
public static class ProfessionalChartService
{
    /// <summary>Render a professional horizontal bar chart with RTL support.</summary>
    public static void RenderBarChart(IContainer container, List<(string Label, int Value)> data, string title, string titleAr, bool isArabic, bool includeChart = true, int maxBars = 15)
    {
        if (!includeChart || data == null || data.Count == 0)
            return;

        var maxValue = data.Max(d => d.Value);
        if (maxValue == 0) maxValue = 1;

        container.PaddingTop(16).Column(column =>
        {
            // Title + accent: do not put wrapped Arabic/English in a single Row with fixed-height bar — QuestPDF
            // reports "conflicting size constraints" when text min-height exceeds the row (Arabic is larger / wraps).
            column.Item().PaddingBottom(8).Column(titleCol =>
            {
                if (isArabic)
                {
                    titleCol.Item().AlignRight()
                        .Text(string.IsNullOrEmpty(titleAr) ? title : titleAr)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 12))
                        .Bold()
                        .LineHeight(1.35f)
                        .FontColor(ProfessionalReportTemplate.Colors.Primary);
                }
                else
                {
                    titleCol.Item()
                        .Text(title)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 12))
                        .Bold()
                        .LineHeight(1.3f)
                        .FontColor(ProfessionalReportTemplate.Colors.Primary);
                }
                titleCol.Item().PaddingTop(6).Element(c =>
                {
                    if (isArabic)
                        c.AlignRight().Width(200).Height(4).Background(ProfessionalReportTemplate.Colors.Primary);
                    else
                        c.AlignLeft().Width(200).Height(4).Background(ProfessionalReportTemplate.Colors.Primary);
                });
            });

            // Border around chart area
            column.Item().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(12).Column(chartCol =>
            {
                var dataToShow = data.Take(maxBars).ToList();
                
                foreach (var (label, value) in dataToShow)
                {
                    // 0..1; QuestPDF throws if RelativeItem(0) is used, so p==0 and p==1 are special-cased.
                    var p = maxValue > 0 ? (float)value / maxValue : 0f;
                    p = Math.Clamp(p, 0f, 1f);

                    var rowPad = isArabic ? 4f : 6f;
                    var trackH = isArabic ? 16f : 18f;
                    chartCol.Item().PaddingBottom(rowPad).Row(row =>
                    {
                        if (isArabic)
                        {
                            // RTL: Value | Bar | Label
                            row.ConstantItem(50).AlignRight().PaddingRight(8)
                                .Text(ArabicFormattingService.FormatNumber(value))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);

                            // Middle slot: MinHeight + AlignMiddle so wrapped Arabic labels do not fight a fixed row height.
                            // PaddingHorizontal only — vertical Padding(2) inside Height(trackH) left ~12px for Height(14) bars (QuestPDF conflict).
                            row.RelativeItem().Element(slot =>
                                ComposeBarTrackSlot(slot, p, value, maxValue, rtl: true, trackH));

                            row.ConstantItem(100).AlignRight().PaddingRight(4)
                                .Text(TruncateLabel(label, 18))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 8))
                                .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                        }
                        else
                        {
                            // LTR: Label | Bar | Value
                            row.ConstantItem(120).PaddingRight(8)
                                .Text(TruncateLabel(label, 20))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);

                            row.RelativeItem().Element(slot =>
                                ComposeBarTrackSlot(slot, p, value, maxValue, rtl: false, trackH));

                            row.ConstantItem(50).AlignRight().PaddingLeft(8)
                                .Text(ArabicFormattingService.FormatNumber(value))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                        }
                    });
                }
            });
        });
    }

    /// <summary>Render a professional daily timeline chart.</summary>
    public static void RenderDailyChart(IContainer container, List<(string Date, int Count)> data, string title, string titleAr, bool isArabic, bool includeChart = true, int maxDays = 31)
    {
        if (!includeChart || data == null || data.Count == 0)
            return;

        var dataToShow = data.Count > maxDays ? data.TakeLast(maxDays).ToList() : data;
        var labels = dataToShow.Select(d => d.Date).ToList();
        var values = dataToShow.Select(d => d.Count).ToList();

        RenderBarChart(container, dataToShow.Select(d => (d.Date, d.Count)).ToList(), title, titleAr, isArabic, includeChart, maxDays);
    }

    /// <summary>Render a professional pie chart representation (using stacked bars for simplicity).</summary>
    public static void RenderPieChart(IContainer container, List<(string Label, int Value, string Color)> data, string title, string titleAr, bool isArabic, bool includeChart = true)
    {
        if (!includeChart || data == null || data.Count == 0)
            return;

        var total = data.Sum(d => d.Value);
        if (total == 0) total = 1;

        container.PaddingTop(16).Column(column =>
        {
            // Chart title
            column.Item().PaddingBottom(12)
                .Text(isArabic && !string.IsNullOrEmpty(titleAr) ? titleAr : title)
                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 12))
                .Bold()
                .FontColor(ProfessionalReportTemplate.Colors.Primary);

            // Stacked bar representation
            column.Item().Height(40).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                .Row(row =>
                {
                    foreach (var (label, value, color) in data)
                    {
                        var widthPercent = total > 0 ? (float)value / total : 0;
                        if (widthPercent > 0.01) // Only show segments > 1%
                        {
                            row.RelativeItem(widthPercent).Background(color);
                        }
                    }
                });

            // Legend: one line per item (a single Row with all segments overflows with Arabic / long text).
            column.Item().PaddingTop(12).Column(legendCol =>
            {
                foreach (var (label, value, color) in data)
                {
                    var percent = total > 0 ? (decimal)value / total * 100 : 0;
                    var legendText = $"{TruncateLabel(label, 24)}: {ArabicFormattingService.FormatPercentage(percent)}";

                    legendCol.Item().PaddingBottom(4).Row(itemRow =>
                    {
                        if (isArabic)
                        {
                            itemRow.RelativeItem(1)
                                .AlignRight()
                                .Text(legendText)
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);

                            itemRow.ConstantItem(16).PaddingRight(4).Height(12).Background(color);
                        }
                        else
                        {
                            itemRow.ConstantItem(16).PaddingRight(4).Height(12).Background(color);

                            itemRow.RelativeItem(1)
                                .Text(legendText)
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                        }
                    });
                }
            });
        });
    }

    /// <summary>Render a trend indicator (line chart simulation with arrows).</summary>
    public static void RenderTrendIndicator(IContainer container, string label, string labelAr, decimal currentValue, decimal previousValue, bool isArabic)
    {
        var change = previousValue > 0 ? ((currentValue - previousValue) / previousValue) * 100 : 0;
        var isImproving = change >= 0;
        var arrow = isImproving ? "↗" : "↘";
        var trendColor = isImproving ? ProfessionalReportTemplate.Colors.Success : ProfessionalReportTemplate.Colors.Error;

        container.Row(row =>
        {
            if (isArabic)
            {
                row.AutoItem().Text($"{arrow} {ArabicFormattingService.FormatPercentage(Math.Abs(change))}")
                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                    .FontColor(trendColor);
                
                row.AutoItem().PaddingRight(8)
                    .Text(string.IsNullOrEmpty(labelAr) ? label : labelAr)
                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                    .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
            }
            else
            {
                row.AutoItem().PaddingRight(8)
                    .Text(label)
                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                    .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                
                row.AutoItem().Text($"{arrow} {ArabicFormattingService.FormatPercentage(Math.Abs(change))}")
                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                    .FontColor(trendColor);
            }
        });
    }

    /// <summary>Middle column of a bar chart row: fixed-height track centered when the row grows (e.g. wrapped labels).</summary>
    private static void ComposeBarTrackSlot(IContainer slot, float p, int value, int maxValue, bool rtl, float trackH)
    {
        slot.MinHeight(trackH).AlignMiddle().Element(inner =>
            inner.Height(trackH)
                .Background(ProfessionalReportTemplate.Colors.Background)
                .PaddingHorizontal(2)
                .Element(track => RenderBarTrackSplit(track, p, value, maxValue, rtl)));
    }

    /// <summary>Bar track: QuestPDF requires every relative weight to be &gt; 0. For p=0 or p=1, render a single full-width segment.</summary>
    private static void RenderBarTrackSplit(IContainer track, float p, int value, int maxValue, bool rtl)
    {
        var color = GetBarColor(value, maxValue);
        const float eps = 0.0001f;
        if (p <= eps)
        {
            track.Row(r => r.RelativeItem(1f).Height(14));
        }
        else if (p >= 1f - eps)
        {
            track.Row(r => r.RelativeItem(1f).Background(color).Height(14));
        }
        else
        {
            if (rtl)
            {
                track.Row(barRow =>
                {
                    barRow.RelativeItem(1f - p);
                    barRow.RelativeItem(p).Background(color).Height(14);
                });
            }
            else
            {
                track.Row(barRow =>
                {
                    barRow.RelativeItem(p).Background(color).Height(14);
                    barRow.RelativeItem(1f - p);
                });
            }
        }
    }

    /// <summary>Get bar color based on value relative to max (gradient from primary to accent).</summary>
    private static string GetBarColor(int value, int maxValue)
    {
        // Use corporate colors based on value intensity
        if (value >= maxValue * 0.8)
            return ProfessionalReportTemplate.Colors.Primary; // Blue.Darken2
        else if (value >= maxValue * 0.5)
            return ProfessionalReportTemplate.Colors.Accent; // Light Blue.Darken1
        else if (value >= maxValue * 0.3)
            return ProfessionalReportTemplate.Colors.Secondary; // Teal.Medium
        else
            return ProfessionalReportTemplate.Colors.TextSecondary; // Grey
    }

    /// <summary>Truncate label to specified length.</summary>
    private static string TruncateLabel(string label, int maxLength)
    {
        if (string.IsNullOrEmpty(label))
            return "";
        
        return label.Length > maxLength ? label[..maxLength] + "..." : label;
    }

    /// <summary>Get color for status-based charts.</summary>
    public static string GetStatusColor(string status)
    {
        return status?.ToLowerInvariant() switch
        {
            "cleared" => ProfessionalReportTemplate.Colors.Success,
            "issue" => ProfessionalReportTemplate.Colors.Error,
            "reviewed" => ProfessionalReportTemplate.Colors.Warning,
            "readyforaudit" => ProfessionalReportTemplate.Colors.Accent,
            "draft" => ProfessionalReportTemplate.Colors.TextSecondary,
            _ => ProfessionalReportTemplate.Colors.Primary
        };
    }

    /// <summary>Get color for priority levels.</summary>
    public static string GetPriorityColor(string priority)
    {
        return priority?.ToLowerInvariant() switch
        {
            "critical" or "urgent" => ProfessionalReportTemplate.Colors.Error,
            "high" => ProfessionalReportTemplate.Colors.Warning,
            "medium" or "normal" => ProfessionalReportTemplate.Colors.Accent,
            "low" => ProfessionalReportTemplate.Colors.Success,
            _ => ProfessionalReportTemplate.Colors.TextSecondary
        };
    }
}
