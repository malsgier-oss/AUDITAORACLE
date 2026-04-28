using System.IO;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Manages branding configuration for professional reports.
/// Provides methods to initialize, update, and retrieve corporate branding settings.
/// </summary>
public static class ReportBrandingConfiguration
{
    // Setting keys
    public const string ReportLogoPath = "report_logo_path";
    public const string ReportOrganizationNameEn = "report_organization_name_en";
    public const string ReportOrganizationNameAr = "report_organization_name_ar";
    public const string ReportPrimaryColor = "report_primary_color";
    public const string ReportSecondaryColor = "report_secondary_color";
    public const string ReportDisclaimerTextEn = "report_disclaimer_text_en";
    public const string ReportDisclaimerTextAr = "report_disclaimer_text_ar";
    public const string ReportDefaultDistributionList = "report_default_distribution_list";
    /// <summary>Optional: prior report ID this run replaces (shown on cover when set).</summary>
    public const string ReportSupersedesReportId = "report_supersedes_report_id";
    public const string ArchiveRetentionYears = "archive_retention_years";

    /// <summary>
    /// Initialize default branding settings if they don't exist.
    /// Called during application startup or settings configuration.
    /// </summary>
    public static void InitializeDefaults(IConfigStore configStore, string? currentUsername = null)
    {
        // Logo path (leave blank, user must configure)
        if (configStore.GetSetting(ReportLogoPath) == null)
        {
            configStore.SetSetting(ReportLogoPath, "", currentUsername);
        }

        // Organization names
        if (configStore.GetSetting(ReportOrganizationNameEn) == null)
        {
            configStore.SetSetting(ReportOrganizationNameEn, "WorkAudit Organization", currentUsername);
        }
        
        if (configStore.GetSetting(ReportOrganizationNameAr) == null)
        {
            configStore.SetSetting(ReportOrganizationNameAr, "مؤسسة WorkAudit", currentUsername);
        }

        // Corporate colors (using professional defaults from template)
        if (configStore.GetSetting(ReportPrimaryColor) == null)
        {
            configStore.SetSetting(ReportPrimaryColor, "#1565C0", currentUsername); // Blue.Darken2
        }
        
        if (configStore.GetSetting(ReportSecondaryColor) == null)
        {
            configStore.SetSetting(ReportSecondaryColor, "#00897B", currentUsername); // Teal.Medium
        }

        // Disclaimer texts
        if (configStore.GetSetting(ReportDisclaimerTextEn) == null)
        {
            var defaultEn = @"This report is confidential and intended solely for internal use by the organization. Distribution is restricted to the Board, Audit Committee, and senior management.

Data presented is based on system records as of the report generation date. This report complies with regulatory requirements for audit documentation.

Unauthorized distribution of this document is prohibited. If received in error, please notify the sender immediately and destroy all copies.";
            configStore.SetSetting(ReportDisclaimerTextEn, defaultEn, currentUsername);
        }
        
        if (configStore.GetSetting(ReportDisclaimerTextAr) == null)
        {
            var defaultAr = @"هذا التقرير سري ومخصص للاستخدام الداخلي من قبل المؤسسة فقط. التوزيع مقتصر على مجلس الإدارة ولجنة التدقيق والإدارة العليا.

البيانات المقدمة مستندة إلى سجلات النظام بتاريخ إنشاء التقرير. يتوافق هذا التقرير مع متطلبات التوثيق التنظيمي.

يحظر التوزيع غير المصرح به لهذا المستند. في حالة الاستلام عن طريق الخطأ، يرجى إخطار المرسل على الفور وإتلاف جميع النسخ.";
            configStore.SetSetting(ReportDisclaimerTextAr, defaultAr, currentUsername);
        }

        // Retention years
        if (configStore.GetSetting(ArchiveRetentionYears) == null)
        {
            configStore.SetSettingInt(ArchiveRetentionYears, 7, currentUsername); // Standard banking retention
        }

        if (configStore.GetSetting(ReportDefaultDistributionList) == null)
        {
            configStore.SetSetting(ReportDefaultDistributionList, "Board, Audit Committee, General Manager, CFO (internal only)", currentUsername);
        }
    }

    /// <summary>
    /// Get all branding settings as a dictionary for easy access.
    /// </summary>
    public static Dictionary<string, string> GetAllBrandingSettings(IConfigStore configStore)
    {
        return new Dictionary<string, string>
        {
            [ReportLogoPath] = configStore.GetSettingValue(ReportLogoPath, "") ?? "",
            [ReportOrganizationNameEn] = configStore.GetSettingValue(ReportOrganizationNameEn, "WorkAudit") ?? "WorkAudit",
            [ReportOrganizationNameAr] = configStore.GetSettingValue(ReportOrganizationNameAr, "WorkAudit") ?? "WorkAudit",
            [ReportPrimaryColor] = configStore.GetSettingValue(ReportPrimaryColor, "#1565C0") ?? "#1565C0",
            [ReportSecondaryColor] = configStore.GetSettingValue(ReportSecondaryColor, "#00897B") ?? "#00897B",
            [ReportDisclaimerTextEn] = configStore.GetSettingValue(ReportDisclaimerTextEn, "") ?? "",
            [ReportDisclaimerTextAr] = configStore.GetSettingValue(ReportDisclaimerTextAr, "") ?? "",
            [ReportDefaultDistributionList] = configStore.GetSettingValue(ReportDefaultDistributionList, "Board, Audit Committee, General Manager, CFO (internal only)") ?? "",
            [ReportSupersedesReportId] = configStore.GetSettingValue(ReportSupersedesReportId, "") ?? "",
            [ArchiveRetentionYears] = configStore.GetSettingInt(ArchiveRetentionYears, 7).ToString()
        };
    }

    /// <summary>
    /// Update logo path setting.
    /// </summary>
    public static bool SetLogoPath(IConfigStore configStore, string logoPath, string? currentUsername = null)
    {
        // Validate file exists if path is not empty
        if (!string.IsNullOrEmpty(logoPath) && !File.Exists(logoPath))
        {
            throw new FileNotFoundException($"Logo file not found: {logoPath}");
        }
        
        return configStore.SetSetting(ReportLogoPath, logoPath, currentUsername);
    }

    /// <summary>
    /// Update organization name (both languages).
    /// </summary>
    public static bool SetOrganizationName(IConfigStore configStore, string nameEn, string nameAr, string? currentUsername = null)
    {
        var success = true;
        success &= configStore.SetSetting(ReportOrganizationNameEn, nameEn, currentUsername);
        success &= configStore.SetSetting(ReportOrganizationNameAr, nameAr, currentUsername);
        return success;
    }

    /// <summary>
    /// Update corporate colors.
    /// </summary>
    public static bool SetCorporateColors(IConfigStore configStore, string primaryColor, string secondaryColor, string? currentUsername = null)
    {
        // Basic validation (hex color format)
        if (!IsValidHexColor(primaryColor) || !IsValidHexColor(secondaryColor))
        {
            throw new ArgumentException("Colors must be in valid hex format (e.g., #1565C0)");
        }
        
        var success = true;
        success &= configStore.SetSetting(ReportPrimaryColor, primaryColor, currentUsername);
        success &= configStore.SetSetting(ReportSecondaryColor, secondaryColor, currentUsername);
        return success;
    }

    /// <summary>
    /// Update disclaimer text (both languages).
    /// </summary>
    public static bool SetDisclaimerText(IConfigStore configStore, string textEn, string textAr, string? currentUsername = null)
    {
        var success = true;
        success &= configStore.SetSetting(ReportDisclaimerTextEn, textEn, currentUsername);
        success &= configStore.SetSetting(ReportDisclaimerTextAr, textAr, currentUsername);
        return success;
    }

    /// <summary>
    /// Update retention period.
    /// </summary>
    public static bool SetRetentionYears(IConfigStore configStore, int years, string? currentUsername = null)
    {
        if (years < 1 || years > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(years), "Retention years must be between 1 and 99");
        }
        
        return configStore.SetSettingInt(ArchiveRetentionYears, years, currentUsername);
    }

    private static bool IsValidHexColor(string color)
    {
        if (string.IsNullOrEmpty(color)) return false;
        if (!color.StartsWith("#")) return false;
        if (color.Length != 7) return false;
        return color[1..].All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
    }

    /// <summary>
    /// Get a formatted configuration summary for display in settings UI.
    /// </summary>
    public static string GetConfigurationSummary(IConfigStore configStore)
    {
        var settings = GetAllBrandingSettings(configStore);
        var summary = "Report Branding Configuration\n";
        summary += "==============================\n\n";
        
        summary += $"Logo Path: {(string.IsNullOrEmpty(settings[ReportLogoPath]) ? "(Not configured)" : settings[ReportLogoPath])}\n";
        summary += $"Organization (EN): {settings[ReportOrganizationNameEn]}\n";
        summary += $"Organization (AR): {settings[ReportOrganizationNameAr]}\n";
        summary += $"Primary Color: {settings[ReportPrimaryColor]}\n";
        summary += $"Secondary Color: {settings[ReportSecondaryColor]}\n";
        summary += $"Retention Period: {settings[ArchiveRetentionYears]} years\n";
        summary += $"Disclaimer (EN): {(string.IsNullOrEmpty(settings[ReportDisclaimerTextEn]) ? "(Not set)" : $"{settings[ReportDisclaimerTextEn][..Math.Min(50, settings[ReportDisclaimerTextEn].Length)]}...")}\n";
        summary += $"Disclaimer (AR): {(string.IsNullOrEmpty(settings[ReportDisclaimerTextAr]) ? "(Not set)" : $"{settings[ReportDisclaimerTextAr][..Math.Min(50, settings[ReportDisclaimerTextAr].Length)]}...")}\n";
        
        return summary;
    }
}
