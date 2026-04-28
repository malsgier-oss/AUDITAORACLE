using System.IO;
using WorkAudit.Domain;

namespace WorkAudit.Core.Helpers;

/// <summary>Resolves stored document paths to an existing file on disk (absolute or relative to base directory).</summary>
public static class DocumentFilePathResolver
{
    public static string? ResolveExistingPath(Document doc, string baseDirectory) =>
        ResolveExistingPath(doc.FilePath, baseDirectory);

    public static string? ResolveExistingPath(string? filePath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var p = filePath.Trim();
        try
        {
            if (File.Exists(p))
                return Path.GetFullPath(p);
        }
        catch
        {
            /* ignore */
        }

        if (!Path.IsPathRooted(p))
        {
            try
            {
                var combined = Path.GetFullPath(Path.Combine(baseDirectory, p));
                if (File.Exists(combined))
                    return combined;
            }
            catch
            {
                /* ignore */
            }
        }

        return null;
    }
}
