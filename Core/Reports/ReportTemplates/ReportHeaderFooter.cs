using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>
/// Shared header and footer template for bank-grade reports.
/// Includes branding, compliance footer (retention, confidentiality), and generation timestamp.
/// Enhanced with RTL support, logo integration, and bilingual capabilities.
/// </summary>
public static class ReportHeaderFooter
{
    /// <summary>Default retention period in years for compliance footer.</summary>
    public const int DefaultRetentionYears = 7;

    /// <summary>Render watermark overlay on page (DRAFT or CONFIDENTIAL). Call from page.Foreground(). RTL-aware.</summary>
    public static void ComposeWatermark(IContainer container, ReportWatermark watermark, bool isArabic = false)
    {
        if (watermark == ReportWatermark.None) return;
        
        var text = watermark == ReportWatermark.Draft 
            ? (isArabic ? "مسودة" : "DRAFT")
            : (isArabic ? "سري" : "CONFIDENTIAL");
        
        container.AlignCenter().AlignMiddle()
            .Rotate(-45)
            .DefaultTextStyle(x => x.FontSize(72).FontColor(Colors.Grey.Lighten2).Bold())
            .Text(text);
    }

    /// <summary>Render professional report header with optional logo.</summary>
    public static void ComposeHeader(IContainer container, string title, string subtitle, int retentionYears = DefaultRetentionYears, bool isArabic = false, string? logoPath = null)
    {
        container.Row(row =>
        {
            if (isArabic)
            {
                // RTL: Logo on right, text on left
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    try
                    {
                        var logoBytes = File.ReadAllBytes(logoPath);
                        row.ConstantItem(60).AlignRight().MaxHeight(40).Image(logoBytes);
                    }
                    catch
                    {
                        // Logo failed to load, continue without it
                    }
                }

                row.RelativeItem().PaddingRight(10).AlignRight().Column(column =>
                {
                    column.Item().AlignRight().Text(title)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 14))
                        .Bold()
                        .FontColor(ProfessionalReportTemplate.Colors.Primary);
                    
                    column.Item().PaddingTop(4).AlignRight()
                        .Text(subtitle)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                        .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                });
            }
            else
            {
                // LTR: Logo on left, text on right
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    try
                    {
                        var logoBytes = File.ReadAllBytes(logoPath);
                        row.ConstantItem(60).AlignLeft().MaxHeight(40).Image(logoBytes);
                    }
                    catch
                    {
                        // Logo failed to load, continue without it
                    }
                }

                row.RelativeItem().PaddingLeft(10).Column(column =>
                {
                    column.Item().Text(title)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 14))
                        .Bold()
                        .FontColor(ProfessionalReportTemplate.Colors.Primary);
                    
                    column.Item().PaddingTop(4)
                        .Text(subtitle)
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                        .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                });
            }
        });
    }

    /// <summary>Render professional report footer with compliance notice and page numbers. RTL-aware.</summary>
    public static void ComposeFooter(IContainer container, int retentionYears = DefaultRetentionYears, bool isArabic = false, string? reportId = null)
    {
        container.AlignCenter()
            .Column(column =>
            {
                // Page numbers and generation info
                column.Item().Row(row =>
                {
                    row.RelativeItem().AlignCenter()
                        .DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 8).FontColor(ProfessionalReportTemplate.Colors.TextSecondary))
                        .Text(text =>
                        {
                            if (isArabic)
                            {
                                text.Span("صفحة ");
                                text.CurrentPageNumber();
                                text.Span(" من ");
                                text.TotalPages();
                                text.Span($"  |  تم الإنشاء {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                            }
                            else
                            {
                                text.Span("Page ");
                                text.CurrentPageNumber();
                                text.Span(" of ");
                                text.TotalPages();
                                text.Span($"  |  Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                            }
                        });
                });

                // Retention policy and confidentiality
                column.Item().PaddingTop(2).AlignCenter()
                    .DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 8).FontColor(ProfessionalReportTemplate.Colors.TextSecondary))
                    .Text(text =>
                    {
                        if (isArabic)
                        {
                            text.Span($"الاحتفاظ لمدة {retentionYears} سنوات  |  سري");
                        }
                        else
                        {
                            text.Span($"Retain per {retentionYears}-year policy  |  CONFIDENTIAL");
                        }
                        
                        if (!string.IsNullOrEmpty(reportId))
                        {
                            text.Span($"  |  ID: {reportId}");
                        }
                    });
            });
    }

    /// <summary>Get retention years from config store, or default.</summary>
    public static int GetRetentionYears(IConfigStore? configStore)
    {
        return configStore?.GetSettingInt("archive_retention_years", DefaultRetentionYears) ?? DefaultRetentionYears;
    }

    /// <summary>Get logo path from config store.</summary>
    public static string? GetLogoPath(IConfigStore? configStore)
    {
        return configStore?.GetSettingValue("report_logo_path", null);
    }

    /// <summary>Get organization name from config store.</summary>
    public static string GetOrganizationName(IConfigStore? configStore, bool isArabic = false)
    {
        var key = isArabic ? "report_organization_name_ar" : "report_organization_name_en";
        var name = configStore?.GetSettingValue(key, null);
        
        if (string.IsNullOrEmpty(name))
        {
            // Fallback to English name
            name = configStore?.GetSettingValue("report_organization_name_en", "WorkAudit");
        }
        
        return name ?? "WorkAudit";
    }
}
