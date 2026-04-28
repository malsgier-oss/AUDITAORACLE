using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace WorkAudit.Domain;

/// <summary>
/// Centralized application color constants for consistent UI theming.
/// </summary>
public static class AppColors
{
    // Status colors
    public static readonly MediaColor Success = MediaColor.FromRgb(0x10, 0x7C, 0x10);
    public static readonly MediaColor Error = MediaColor.FromRgb(0xE8, 0x11, 0x23);
    public static readonly MediaColor Warning = MediaColor.FromRgb(0xFF, 0xB9, 0x00);

    // Background colors
    public static readonly MediaColor DialogBackground = MediaColor.FromRgb(0xF5, 0xF5, 0xF5);
    public static readonly MediaColor PanelBackground = MediaColor.FromRgb(0xF8, 0xF8, 0xF8);

    // Brushes for common use
    public static readonly SolidColorBrush SuccessBrush = new(Success);
    public static readonly SolidColorBrush ErrorBrush = new(Error);
    public static readonly SolidColorBrush WarningBrush = new(Warning);
    public static readonly SolidColorBrush DialogBackgroundBrush = new(DialogBackground);
    public static readonly SolidColorBrush PanelBackgroundBrush = new(PanelBackground);

    // Freeze brushes for thread safety
    static AppColors()
    {
        SuccessBrush.Freeze();
        ErrorBrush.Freeze();
        WarningBrush.Freeze();
        DialogBackgroundBrush.Freeze();
        PanelBackgroundBrush.Freeze();
    }
}
