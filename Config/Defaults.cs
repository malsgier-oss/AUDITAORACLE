using System.IO;
using System.Reflection;

namespace WorkAudit.Config;

/// <summary>
/// Default configuration values.
/// </summary>
public static class Defaults
{
    /// <summary>
    /// Same value as the built assembly (from <c>build/WorkAudit.Version.props</c> via <c>AssemblyInformationalVersion</c>).
    /// Keeps Control Panel, file properties, and in-app version aligned for both <c>dotnet run</c> and installed builds.
    /// </summary>
    public static string AppVersion => _appVersion.Value;

    private static readonly Lazy<string> _appVersion = new(ReadAssemblyVersion);

    private static string ReadAssemblyVersion()
    {
        var asm = typeof(Defaults).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus].Trim() : info.Trim();
        }

        var v = asm.GetName().Version;
        return v?.ToString(3) ?? "0.0.0";
    }
    public const string DefaultBaseDirName = "WORKAUDIT_Docs";

    public static string GetDefaultBaseDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, DefaultBaseDirName);
    }

    /// <summary>
    /// Returns true if the path's root (drive or UNC) appears ready so <see cref="Directory.CreateDirectory"/> can succeed.
    /// </summary>
    public static bool IsBaseDirectoryAccessible(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            if (root.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (root.Length >= 2 && root[1] == ':')
            {
                try
                {
                    return new DriveInfo(root).IsReady;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uses <paramref name="preferred"/> when accessible; otherwise <see cref="GetDefaultBaseDir"/>.
    /// </summary>
    public static string ResolveBaseDirectory(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && IsBaseDirectoryAccessible(preferred))
            return Path.GetFullPath(preferred.Trim());
        return GetDefaultBaseDir();
    }

    public static string GetUserSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WORKAUDIT");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "user_settings.json");
    }
}
