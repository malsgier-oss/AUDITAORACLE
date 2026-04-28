using System.IO;
using Microsoft.Web.WebView2.Core;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// WebView2 can use the machine-wide Evergreen runtime (separate install) or a fixed-version
/// folder shipped next to the app under <c>WebView2Runtime</c> (no separate install).
/// </summary>
public static class WebView2EnvironmentHelper
{
    private const string BundledRuntimeFolderName = "WebView2Runtime";

    /// <summary>
    /// Returns the directory that contains <c>msedgewebview2.exe</c> when a fixed runtime is
    /// bundled with the app, or <c>null</c> to use the Evergreen runtime (dev machines).
    /// </summary>
    public static string? GetFixedVersionBrowserDirectory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, BundledRuntimeFolderName);
        if (!Directory.Exists(root))
            return null;

        var direct = Path.Combine(root, "msedgewebview2.exe");
        if (File.Exists(direct))
            return root;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var exe = Path.Combine(dir, "msedgewebview2.exe");
            if (File.Exists(exe))
                return dir;
        }

        return null;
    }

    public static string GetUserDataFolder(string subFolder)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUDITA",
            "WebView2",
            subFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static Task<CoreWebView2Environment> CreateForAppAsync(string userDataSubFolder)
    {
        var browserDir = GetFixedVersionBrowserDirectory();
        var userData = GetUserDataFolder(userDataSubFolder);
        return CoreWebView2Environment.CreateAsync(browserDir, userData);
    }
}
