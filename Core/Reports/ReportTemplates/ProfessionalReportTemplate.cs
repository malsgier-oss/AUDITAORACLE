using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>
/// Professional report template foundation for executive-grade, bilingual reports.
/// Provides RTL support, corporate branding, and reusable components for boardroom-quality output.
/// </summary>
public static class ProfessionalReportTemplate
{
    // Corporate Color Scheme
    public static class Colors
    {
        public static string Primary => "#1565C0";        // Blue.Darken2
        public static string Secondary => "#00897B";      // Teal.Medium
        public static string Success => "#43A047";        // Green.Darken1
        public static string Warning => "#FB8C00";        // Orange.Darken1
        public static string Error => "#E53935";          // Red.Medium
        public static string Background => "#FAFAFA";     // Grey.Lighten5
        public static string TextPrimary => "#212121";    // Grey.Darken4
        public static string TextSecondary => "#757575";  // Grey.Darken1
        public static string Border => "#E0E0E0";         // Grey.Lighten2
        public static string Accent => "#0288D1";         // Light Blue.Darken1
    }

    /// <summary>Section information for table of contents.</summary>
    public class Section
    {
        public string Title { get; set; } = "";
        public string TitleAr { get; set; } = "";
        public int PageNumber { get; set; }
        public List<Section> SubSections { get; set; } = new();
    }

    /// <summary>Report metadata for cover page and attestation.</summary>
    public class ReportMetadata
    {
        public string ReportId { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleAr { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public string OrganizationName { get; set; } = "";
        public string OrganizationNameAr { get; set; } = "";
        public string ConfidentialityLevel { get; set; } = "CONFIDENTIAL";
        public string Version { get; set; } = "v1.0";
        public string DistributionListSummary { get; set; } = "";
        /// <summary>When set, cover shows "Supersedes" lineage (e.g. prior report UUID or internal ref).</summary>
        public string SupersedesReportId { get; set; } = "";
        public ReportAttestation? Attestation { get; set; }
    }

    /// <summary>Apply language-specific settings to page.</summary>
    public static void ApplyLanguageSettings(PageDescriptor page, bool isArabic)
    {
        // Note: RTL layout is handled at the text/container level using AlignRight() and manual ordering
        // QuestPDF's ContentDirection may not be available in all versions
        // RTL support is primarily achieved through:
        // 1. Text alignment (.AlignRight() for Arabic)
        // 2. Manual column reordering in tables
        // 3. Right-to-left padding/margin adjustments
    }

    /// <summary>Get text style with appropriate font for language.</summary>
    public static TextStyle GetTextStyle(bool isArabic, int fontSize = 10)
    {
        // Note: Font loading is handled by ReportFontService
        // QuestPDF will use default fonts, but we'll specify font family when available
        var style = TextStyle.Default.FontSize(fontSize);
        
        if (isArabic)
        {
            // Arabic fonts: Tahoma, Arabic Typesetting, Traditional Arabic
            // These are typically available on Windows systems
            style = style.FontFamily("Tahoma, 'Arabic Typesetting', 'Traditional Arabic', sans-serif");
        }
        else
        {
            style = style.FontFamily("Segoe UI, Calibri, Arial, sans-serif");
        }
        
        return style;
    }

    /// <summary>Render professional cover page.</summary>
    public static void RenderCoverPage(IContainer container, ReportMetadata metadata, bool isArabic, string? logoPath = null)
    {
        container.Column(column =>
        {
            column.Spacing(20);

            // Logo (if available)
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                try
                {
                    var logoBytes = File.ReadAllBytes(logoPath);
                    column.Item().AlignCenter().PaddingTop(60).MaxWidth(200).Image(logoBytes);
                }
                catch
                {
                    // Logo loading failed, skip
                }
            }
            else
            {
                // Placeholder space
                column.Item().PaddingTop(100);
            }

            // Organization name
            var orgName = isArabic && !string.IsNullOrEmpty(metadata.OrganizationNameAr) 
                ? metadata.OrganizationNameAr 
                : metadata.OrganizationName;
            
            if (!string.IsNullOrEmpty(orgName))
            {
                column.Item().AlignCenter().Text(orgName)
                    .Style(GetTextStyle(isArabic, 16))
                    .Bold()
                    .FontColor(Colors.Primary);
            }

            // Report title
            column.Item().PaddingTop(40).AlignCenter()
                .Text(isArabic && !string.IsNullOrEmpty(metadata.TitleAr) ? metadata.TitleAr : metadata.Title)
                .Style(GetTextStyle(isArabic, 28))
                .Bold()
                .FontColor(Colors.TextPrimary);

            // Date range
            var dateRangeText = isArabic
                ? $"{ArabicFormattingService.FormatDate(metadata.DateTo)} إلى {ArabicFormattingService.FormatDate(metadata.DateFrom)}"
                : $"{metadata.DateFrom:yyyy-MM-dd} to {metadata.DateTo:yyyy-MM-dd}";
            
            column.Item().AlignCenter().PaddingTop(20)
                .Text(dateRangeText)
                .Style(GetTextStyle(isArabic, 14))
                .FontColor(Colors.TextSecondary);

            // Decorative accent bar
            column.Item().PaddingTop(40).AlignCenter()
                .Width(200).Height(4)
                .Background(Colors.Primary);

            // Metadata box
            column.Item().PaddingTop(60).AlignCenter().MaxWidth(400)
                .Border(1).BorderColor(Colors.Border)
                .Padding(20)
                .Column(metaCol =>
                {
                    metaCol.Spacing(8);

                    // Report type
                    metaCol.Item().Row(row =>
                    {
                        var typeLabel = isArabic ? "النوع:" : "Type:";
                        row.AutoItem().Text(typeLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text(metadata.Type).Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });

                    // Report ID
                    metaCol.Item().Row(row =>
                    {
                        var idLabel = isArabic ? "المعرف:" : "Report ID:";
                        row.AutoItem().Text(idLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text(metadata.ReportId).Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });

                    // Version
                    metaCol.Item().Row(row =>
                    {
                        var versionLabel = isArabic ? "الإصدار:" : "Version:";
                        row.AutoItem().Text(versionLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text(metadata.Version).Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });

                    // Generated by
                    metaCol.Item().Row(row =>
                    {
                        var byLabel = isArabic ? "أعده:" : "Generated by:";
                        row.AutoItem().Text(byLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text($"{metadata.GeneratedBy} on {metadata.GeneratedAt:yyyy-MM-dd HH:mm}").Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });

                    if (!string.IsNullOrWhiteSpace(metadata.DistributionListSummary))
                    {
                        metaCol.Item().Row(row =>
                        {
                            var dLabel = isArabic ? "التوزيع:" : "Distribution:";
                            row.AutoItem().Text(dLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                            row.RelativeItem().Text(metadata.DistributionListSummary).Style(GetTextStyle(isArabic, 9)).FontColor(Colors.TextSecondary);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.SupersedesReportId))
                    {
                        metaCol.Item().Row(row =>
                        {
                            var sLabel = isArabic ? "يحل محل:" : "Supersedes:";
                            row.AutoItem().Text(sLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                            row.RelativeItem().Text(metadata.SupersedesReportId).Style(GetTextStyle(isArabic, 9)).FontColor(Colors.TextSecondary);
                        });
                    }

                    // Confidentiality
                    metaCol.Item().PaddingTop(10).AlignCenter()
                        .Text(metadata.ConfidentialityLevel)
                        .Style(GetTextStyle(isArabic, 12))
                        .Bold()
                        .FontColor(Colors.Error);
                });
        });
    }

    /// <summary>Render table of contents.</summary>
    public static void RenderTableOfContents(IContainer container, List<Section> sections, bool isArabic)
    {
        container.Column(column =>
        {
            // TOC Header
            var tocTitle = isArabic ? "جدول المحتويات" : "Table of Contents";
            column.Item().PaddingBottom(20)
                .Text(tocTitle)
                .Style(GetTextStyle(isArabic, 20))
                .Bold()
                .FontColor(Colors.Primary);

            // Decorative line
            column.Item().PaddingBottom(20)
                .Height(2)
                .Background(Colors.Border);

            // TOC entries
            foreach (var section in sections)
            {
                RenderTocEntry(column.Item(), section, isArabic, 0);
            }
        });
    }

    private static void RenderTocEntry(IContainer container, Section section, bool isArabic, int level)
    {
        var indent = level * 20;
        var title = isArabic && !string.IsNullOrEmpty(section.TitleAr) ? section.TitleAr : section.Title;
        
        container.PaddingLeft(indent).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem()
                .Text(title)
                .Style(GetTextStyle(isArabic, 11))
                .FontColor(level == 0 ? Colors.TextPrimary : Colors.TextSecondary);

            // Dot leaders
            row.AutoItem().PaddingHorizontal(8)
                .Text("  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .")
                .Style(GetTextStyle(isArabic, 10))
                .FontColor(Colors.Border);

            row.AutoItem()
                .Text(section.PageNumber.ToString())
                .Style(GetTextStyle(isArabic, 11))
                .FontColor(Colors.TextSecondary);
        });

        // Render subsections
        foreach (var sub in section.SubSections)
        {
            RenderTocEntry(container, sub, isArabic, level + 1);
        }
    }

    /// <summary>Render section divider with colored accent bar.</summary>
    public static void RenderSectionDivider(IContainer container, string title, string titleAr, bool isArabic, string? icon = null)
    {
        container.PaddingTop(20).PaddingBottom(12).Row(row =>
        {
            if (isArabic)
            {
                // RTL: accent bar on the right
                row.RelativeItem().AlignRight().PaddingRight(12)
                    .Text(string.IsNullOrEmpty(titleAr) ? title : titleAr)
                    .Style(GetTextStyle(isArabic, 15))
                    .Bold()
                    .FontColor(Colors.Primary);

                row.ConstantItem(4).Background(Colors.Primary);
            }
            else
            {
                // LTR: accent bar on the left
                row.ConstantItem(4).Background(Colors.Primary);

                row.RelativeItem().PaddingLeft(12)
                    .Text(icon != null ? $"{icon} {title}" : title)
                    .Style(GetTextStyle(isArabic, 15))
                    .Bold()
                    .FontColor(Colors.Primary);
            }
        });
    }

    /// <summary>Render attestation section showing approval workflow.</summary>
    /// <param name="suppressHashDisplay">When true, the PDF hash is not shown (e.g. appendix generated before merge; <see cref="IReportAttestationService.RefreshFileHash"/> updates the stored full-file digest).</param>
    public static void RenderAttestationSection(IContainer container, ReportAttestation? attestation, bool isArabic, bool suppressHashDisplay = false)
    {
        if (attestation == null) return;

        container.Border(1).BorderColor(Colors.Border)
            .Padding(20)
            .Column(column =>
            {
                column.Spacing(12);

                // Section title
                var title = isArabic ? "توثيق التقرير" : "Report Attestation";
                column.Item().Text(title)
                    .Style(GetTextStyle(isArabic, 14))
                    .Bold()
                    .FontColor(Colors.Primary);

                // Report ID and Hash
                column.Item().Row(row =>
                {
                    var idLabel = isArabic ? "معرف التقرير:" : "Report ID:";
                    row.AutoItem().MinWidth(100).Text(idLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                    row.RelativeItem().Text(attestation.Uuid).Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                });

                if (!suppressHashDisplay)
                {
                    column.Item().Row(row =>
                    {
                        var hashLabel = isArabic ? "توقيع التحقق:" : "Verification Hash:";
                        row.AutoItem().MinWidth(100).Text(hashLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text(attestation.Sha256Hash?.Substring(0, Math.Min(16, attestation.Sha256Hash.Length)) + "...").Style(GetTextStyle(isArabic, 9)).FontColor(Colors.TextSecondary);
                    });
                }
                else
                {
                    column.Item().Text(isArabic
                            ? "مُسجّل سجل التوثيق في WorkAudit. استخدم التحقق من الملف لعرض SHA-256 الكامل بعد إرفاق الصفحة."
                            : "Digest is recorded in WorkAudit. Use Verify in the app for the full SHA-256 of the final PDF (including this page).")
                        .Style(GetTextStyle(isArabic, 9))
                        .Italic()
                        .FontColor(Colors.TextSecondary);
                }

                // Divider
                column.Item().PaddingVertical(8).Height(1).Background(Colors.Border);

                // Prepared by
                column.Item().Row(row =>
                {
                    var preparedLabel = isArabic ? "أعده:" : "Prepared by:";
                    row.AutoItem().MinWidth(100).Text(preparedLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                    row.RelativeItem().Text($"{attestation.GeneratedByUsername ?? "N/A"} on {attestation.GeneratedAt}").Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                });

                // Reviewed by (if reviewed)
                if (attestation.Status == AttestationStatus.Reviewed || attestation.Status == AttestationStatus.Approved)
                {
                    column.Item().Row(row =>
                    {
                        var reviewedLabel = isArabic ? "راجعه:" : "Reviewed by:";
                        row.AutoItem().MinWidth(100).Text(reviewedLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text($"{attestation.ReviewedByUsername ?? "N/A"} on {attestation.ReviewedAt}").Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });
                }

                // Approved by (if approved)
                if (attestation.Status == AttestationStatus.Approved)
                {
                    column.Item().Row(row =>
                    {
                        var approvedLabel = isArabic ? "اعتمده:" : "Approved by:";
                        row.AutoItem().MinWidth(100).Text(approvedLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                        row.RelativeItem().Text($"{attestation.ApprovedByUsername ?? "N/A"} on {attestation.ApprovedAt}").Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                    });
                }

                // Status indicator
                column.Item().PaddingTop(8).Row(row =>
                {
                    var statusLabel = isArabic ? "الحالة:" : "Status:";
                    row.AutoItem().MinWidth(100).Text(statusLabel).Style(GetTextStyle(isArabic, 10)).Bold();
                    
                    var statusText = attestation.Status == AttestationStatus.Approved ? (isArabic ? "✓ معتمد" : "✓ APPROVED")
                        : attestation.Status == AttestationStatus.Reviewed ? (isArabic ? "تمت المراجعة" : "REVIEWED")
                        : (isArabic ? "تم الإنشاء" : "GENERATED");
                    
                    var statusColor = attestation.Status == AttestationStatus.Approved ? Colors.Success
                        : attestation.Status == AttestationStatus.Reviewed ? Colors.Warning
                        : Colors.TextSecondary;
                    
                    row.RelativeItem().Text(statusText).Style(GetTextStyle(isArabic, 11)).Bold().FontColor(statusColor);
                });

                // Signature placeholders (if not approved)
                if (attestation.Status != AttestationStatus.Approved)
                {
                    column.Item().PaddingTop(20).Column(sigCol =>
                    {
                        var sigLabel = isArabic ? "التوقيع:" : "Signature:";
                        sigCol.Item().Text(sigLabel).Style(GetTextStyle(isArabic, 10)).FontColor(Colors.TextSecondary);
                        sigCol.Item().PaddingTop(20).Height(1).BorderBottom(1).BorderColor(Colors.TextSecondary);
                    });
                }
            });
    }

    /// <summary>Render regulatory disclaimer section.</summary>
    public static void RenderDisclaimerSection(IContainer container, string disclaimerText, bool isArabic)
    {
        if (string.IsNullOrEmpty(disclaimerText)) return;

        container.Border(1).BorderColor(Colors.Border)
            .Background(Colors.Background)
            .Padding(15)
            .Column(column =>
            {
                var title = isArabic ? "إخلاء المسؤولية" : "Disclaimer";
                column.Item().Text(title)
                    .Style(GetTextStyle(isArabic, 12))
                    .Bold()
                    .FontColor(Colors.TextPrimary);

                column.Item().PaddingTop(10)
                    .Text(disclaimerText)
                    .Style(GetTextStyle(isArabic, 9))
                    .FontColor(Colors.TextSecondary)
                    .LineHeight(1.4f);
            });
    }

    /// <summary>Render KPI card component.</summary>
    public static void RenderKpiCard(IContainer container, string label, string labelAr, string value, string trend, bool isArabic, string color)
    {
        container.Border(1).BorderColor(Colors.Border)
            .Background(QuestPDF.Helpers.Colors.White)
            .Padding(15)
            .Column(column =>
            {
                column.Spacing(8);

                // Label
                column.Item().Text(isArabic && !string.IsNullOrEmpty(labelAr) ? labelAr : label)
                    .Style(GetTextStyle(isArabic, 9))
                    .FontColor(Colors.TextSecondary);

                // Value (large, bold, colored)
                column.Item().Text(value)
                    .Style(GetTextStyle(isArabic, 24))
                    .Bold()
                    .FontColor(color);

                // Trend indicator (if provided)
                if (!string.IsNullOrEmpty(trend))
                {
                    column.Item().Text(trend)
                        .Style(GetTextStyle(isArabic, 10))
                        .FontColor(Colors.TextSecondary);
                }
            });
    }

    /// <summary>Get default disclaimer text for banking reports.</summary>
    public static string GetDefaultDisclaimer(bool isArabic, string organizationName)
    {
        if (isArabic)
        {
            return $@"هذا التقرير سري ومخصص للاستخدام الداخلي من قبل {organizationName} فقط. التوزيع مقتصر على مجلس الإدارة ولجنة التدقيق والإدارة العليا.

البيانات المقدمة مستندة إلى سجلات النظام بتاريخ {DateTime.UtcNow:yyyy-MM-dd}. يتوافق هذا التقرير مع متطلبات البنك المركزي للتوثيق الفصلي للمراجعة.

يحظر التوزيع غير المصرح به لهذا المستند. في حالة الاستلام عن طريق الخطأ، يرجى إخطار المرسل على الفور وإتلاف جميع النسخ.";
        }
        else
        {
            return $@"This report is confidential and intended solely for internal use by {organizationName}. Distribution is restricted to the Board, Audit Committee, and senior management.

Data presented is based on system records as of {DateTime.UtcNow:yyyy-MM-dd}. This report complies with Central Bank requirements for quarterly audit documentation.

Unauthorized distribution of this document is prohibited. If received in error, please notify the sender immediately and destroy all copies.";
        }
    }
}
