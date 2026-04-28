using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace WorkAudit.Core.Import;

/// <summary>
/// Picks the next numeric suffix for files in a folder sharing a common name prefix (e.g. Type_20260409_1.jpg).
/// </summary>
public static class DocumentFileNaming
{
    private static readonly ConcurrentDictionary<string, object> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the next integer N such that no file in <paramref name="directory"/> exists with a base name
    /// <c>{prefix}{N}</c> where N is decimal digits. Creates <paramref name="directory"/> if missing.
    /// Thread-safe per directory for concurrent imports.
    /// </summary>
    public static int GetNextSequenceInDirectory(string directory, string fileNamePrefix)
    {
        if (string.IsNullOrEmpty(directory))
            return 1;

        var fullDir = Path.GetFullPath(directory);
        var gate = DirectoryLocks.GetOrAdd(fullDir, _ => new object());
        lock (gate)
        {
            if (!Directory.Exists(fullDir))
                Directory.CreateDirectory(fullDir);

            var max = 0;
            foreach (var file in Directory.EnumerateFiles(fullDir))
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (baseName.Length <= fileNamePrefix.Length)
                    continue;
                if (!baseName.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var suffix = baseName[fileNamePrefix.Length..];
                if (suffix.Length > 0 && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    max = Math.Max(max, n);
            }

            return max + 1;
        }
    }
}
