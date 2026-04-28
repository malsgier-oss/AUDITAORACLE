using System.IO;
using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Security;

/// <summary>
/// Securely overwrites file contents before deletion to reduce recovery risk.
/// Phase 7.1 Data Security: Overwrite deleted files.
/// </summary>
public interface ISecureDeleteService
{
    /// <summary>Overwrites then deletes the file. Returns true if the file was removed.</summary>
    bool SecureDelete(string? filePath);

    /// <summary>Overwrites then deletes the file asynchronously. Returns true if the file was removed.</summary>
    Task<bool> SecureDeleteAsync(string? filePath, CancellationToken ct = default);
}

public class SecureDeleteService : ISecureDeleteService
{
    private const int Passes = 3;
    private const int BufferSize = 64 * 1024;

    private readonly ILogger _log = LoggingService.ForContext<SecureDeleteService>();

    public bool SecureDelete(string? filePath)
    {
        return SecureDeleteCore(filePath);
    }

    public async Task<bool> SecureDeleteAsync(string? filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        if (!File.Exists(filePath))
            return true;

        return await Task.Run(() => SecureDeleteCore(filePath), ct);
    }

    private bool SecureDeleteCore(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        if (!File.Exists(filePath))
            return true;

        try
        {
            var fi = new FileInfo(filePath);
            var length = fi.Length;
            var bufferSize = length > 0 ? Math.Min(BufferSize, (int)Math.Min(length, int.MaxValue)) : BufferSize;
            var buffer = new byte[bufferSize];

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None, bufferSize, FileOptions.DeleteOnClose))
            {
                for (var pass = 0; pass < Passes; pass++)
                {
                    fs.Position = 0;
                    FillBuffer(buffer, pass);
                    var remaining = length;
                    while (remaining > 0)
                    {
                        var toWrite = (int)Math.Min(remaining, buffer.Length);
                        fs.Write(buffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(true);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Secure overwrite failed for {Path}, attempting normal delete", filePath);
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void FillBuffer(byte[] buffer, int pass)
    {
        if (pass == 0)
        {
            Array.Clear(buffer, 0, buffer.Length);
            return;
        }
        if (pass == 1)
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = 0xFF;
            return;
        }
        RandomNumberGenerator.Fill(buffer);
    }
}
