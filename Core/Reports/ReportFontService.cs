using System.IO;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Font management service for professional reports with Arabic support.
/// Handles font discovery, validation, and provides fallback options.
/// </summary>
public static class ReportFontService
{
    private static readonly ILogger Log = LoggingService.ForContext(typeof(ReportFontService));

    /// <summary>Common Arabic font names available on Windows.</summary>
    public static class ArabicFonts
    {
        public const string Tahoma = "Tahoma";
        public const string ArabicTypesetting = "Arabic Typesetting";
        public const string TraditionalArabic = "Traditional Arabic";
        public const string SimplifiedArabic = "Simplified Arabic";
        public const string AdobeArabic = "Adobe Arabic";
    }

    /// <summary>Common English font names for professional documents.</summary>
    public static class EnglishFonts
    {
        public const string SegoeUI = "Segoe UI";
        public const string Calibri = "Calibri";
        public const string Arial = "Arial";
        public const string TimesNewRoman = "Times New Roman";
    }

    /// <summary>Get preferred Arabic font path from Windows Fonts directory.</summary>
    public static string? GetArabicFontPath()
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        
        // Try to find Tahoma (most widely available)
        var tahomaPath = Path.Combine(fontsDir, "TAHOMA.TTF");
        if (File.Exists(tahomaPath))
        {
            Log.Debug("Found Arabic font: Tahoma at {Path}", tahomaPath);
            return tahomaPath;
        }

        // Try Arabic Typesetting
        var arabicTypePath = Path.Combine(fontsDir, "ARABTYPE.TTF");
        if (File.Exists(arabicTypePath))
        {
            Log.Debug("Found Arabic font: Arabic Typesetting at {Path}", arabicTypePath);
            return arabicTypePath;
        }

        // Try Traditional Arabic
        var tradArabicPath = Path.Combine(fontsDir, "TRADARB.TTF");
        if (File.Exists(tradArabicPath))
        {
            Log.Debug("Found Arabic font: Traditional Arabic at {Path}", tradArabicPath);
            return tradArabicPath;
        }

        Log.Warning("No Arabic fonts found in Windows Fonts directory. Arabic text may not render correctly.");
        return null;
    }

    /// <summary>Get preferred English font path from Windows Fonts directory.</summary>
    public static string? GetEnglishFontPath()
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        
        // Try Segoe UI (Windows default)
        var segoeUiPath = Path.Combine(fontsDir, "SEGOEUI.TTF");
        if (File.Exists(segoeUiPath))
        {
            Log.Debug("Found English font: Segoe UI at {Path}", segoeUiPath);
            return segoeUiPath;
        }

        // Try Calibri
        var calibriPath = Path.Combine(fontsDir, "CALIBRI.TTF");
        if (File.Exists(calibriPath))
        {
            Log.Debug("Found English font: Calibri at {Path}", calibriPath);
            return calibriPath;
        }

        // Try Arial (universal fallback)
        var arialPath = Path.Combine(fontsDir, "ARIAL.TTF");
        if (File.Exists(arialPath))
        {
            Log.Debug("Found English font: Arial at {Path}", arialPath);
            return arialPath;
        }

        Log.Warning("No English fonts found in Windows Fonts directory. Will use system default.");
        return null;
    }

    /// <summary>Get font family string for QuestPDF based on language.</summary>
    public static string GetFontFamilyString(bool isArabic)
    {
        if (isArabic)
        {
            // Arabic font stack with fallbacks
            return $"{ArabicFonts.Tahoma}, '{ArabicFonts.ArabicTypesetting}', '{ArabicFonts.TraditionalArabic}', sans-serif";
        }
        else
        {
            // English font stack with fallbacks
            return $"{EnglishFonts.SegoeUI}, {EnglishFonts.Calibri}, {EnglishFonts.Arial}, sans-serif";
        }
    }

    /// <summary>Validate that required fonts are available on the system.</summary>
    public static (bool HasArabicFonts, bool HasEnglishFonts, List<string> MissingFonts) ValidateFonts()
    {
        var missingFonts = new List<string>();
        var hasArabic = GetArabicFontPath() != null;
        var hasEnglish = GetEnglishFontPath() != null;

        if (!hasArabic)
        {
            missingFonts.Add("Arabic fonts (Tahoma, Arabic Typesetting, or Traditional Arabic)");
            Log.Warning("Arabic fonts not available. Arabic text may not render correctly in reports.");
        }

        if (!hasEnglish)
        {
            missingFonts.Add("English fonts (Segoe UI, Calibri, or Arial)");
            Log.Warning("Preferred English fonts not available. System default will be used.");
        }

        return (hasArabic, hasEnglish, missingFonts);
    }

    /// <summary>Get font installation instructions for administrators.</summary>
    public static string GetFontInstallationInstructions()
    {
        return @"Arabic Font Installation Instructions:
===========================================

For optimal Arabic report rendering, install one of the following fonts:

1. Tahoma (Recommended - widely available on Windows)
   - Usually pre-installed on Windows systems
   - Location: C:\Windows\Fonts\TAHOMA.TTF

2. Arabic Typesetting (Professional appearance)
   - Download from Microsoft Typography website
   - Install via: Settings > Personalization > Fonts

3. Traditional Arabic (Classic styling)
   - Available in Microsoft Office installations
   - Install via: Settings > Personalization > Fonts

After installing fonts, restart the application for changes to take effect.

Current font status can be checked via the Tools > System Diagnostics menu.
";
    }

    /// <summary>Check if a specific font is available on the system.</summary>
    public static bool IsFontAvailable(string fontName)
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        
        // Common font filename patterns
        var possibleFiles = new[]
        {
            $"{fontName.ToUpperInvariant().Replace(" ", "")}.TTF",
            $"{fontName.ToUpperInvariant().Replace(" ", "")}.ttf",
            $"{fontName.Replace(" ", "")}.ttf",
            $"{fontName.Replace(" ", "").ToUpperInvariant()}.TTF"
        };

        foreach (var file in possibleFiles)
        {
            var path = Path.Combine(fontsDir, file);
            if (File.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Get diagnostic information about available fonts.</summary>
    public static string GetFontDiagnostics()
    {
        var (hasArabic, hasEnglish, missing) = ValidateFonts();
        
        var diagnostics = "Font Availability Report\n";
        diagnostics += "========================\n\n";
        
        diagnostics += $"Arabic Fonts: {(hasArabic ? "✓ Available" : "✗ Not Found")}\n";
        if (hasArabic)
        {
            var path = GetArabicFontPath();
            diagnostics += $"  Path: {path}\n";
        }
        
        diagnostics += $"\nEnglish Fonts: {(hasEnglish ? "✓ Available" : "✗ Not Found")}\n";
        if (hasEnglish)
        {
            var path = GetEnglishFontPath();
            diagnostics += $"  Path: {path}\n";
        }

        if (missing.Any())
        {
            diagnostics += "\nMissing Fonts:\n";
            foreach (var font in missing)
            {
                diagnostics += $"  - {font}\n";
            }
            diagnostics += "\nRefer to installation instructions for guidance.\n";
        }
        else
        {
            diagnostics += "\n✓ All required fonts are available.\n";
        }

        return diagnostics;
    }

    /// <summary>Log font availability at application startup.</summary>
    public static void LogFontStatus()
    {
        var (hasArabic, hasEnglish, missing) = ValidateFonts();
        
        if (hasArabic && hasEnglish)
        {
            Log.Information("Font check: All required fonts available for bilingual reports");
        }
        else
        {
            Log.Warning("Font check: Missing fonts - {MissingCount} font categories unavailable. Missing: {Missing}", 
                missing.Count, string.Join(", ", missing));
        }
    }
}
