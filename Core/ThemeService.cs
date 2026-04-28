using System;
using System.Windows;
using WorkAudit.Config;

namespace WorkAudit.Core;

/// <summary>
/// Service for applying Light and Dark Midnight themes at runtime.
/// Theme preference is persisted in UserSettings under key "theme".
/// </summary>
public static class ThemeService
{
    public const string ThemeLight = "light";
    public const string ThemeDarkMidnight = "dark_midnight";

    private static ResourceDictionary? _currentThemeDictionary;

    /// <summary>
    /// Applies the theme from UserSettings, or "light" if not set.
    /// </summary>
    public static void ApplySavedTheme()
    {
        var theme = UserSettings.Get<string>("theme", ThemeLight) ?? ThemeLight;
        ApplyTheme(theme);
    }

    /// <summary>
    /// Applies the specified theme and optionally saves it to UserSettings.
    /// </summary>
    /// <param name="themeId">"light" or "dark_midnight"</param>
    /// <param name="save">Whether to persist the theme preference</param>
    public static void ApplyTheme(string themeId, bool save = false)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            themeId = ThemeLight;

        var normalizedId = themeId.Trim().ToLowerInvariant();
        if (normalizedId != ThemeLight && normalizedId != ThemeDarkMidnight)
            normalizedId = ThemeLight;

        var uri = normalizedId == ThemeDarkMidnight
            ? new Uri("pack://application:,,,/Audita;component/Themes/DarkMidnightTheme.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Audita;component/Themes/LightTheme.xaml", UriKind.Absolute);

        var app = Application.Current;
        if (app == null)
            return;

        var resources = app.Resources;
        while (resources.MergedDictionaries.Count > 0)
        {
            resources.MergedDictionaries.RemoveAt(0);
        }
        var dict = new ResourceDictionary { Source = uri };
        resources.MergedDictionaries.Add(dict);
        _currentThemeDictionary = dict;

        if (save)
            UserSettings.Set("theme", normalizedId);
    }
}
