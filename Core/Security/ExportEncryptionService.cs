using System.IO;
using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Security;

/// <summary>
/// AES-256 encryption for secure export files. Uses PBKDF2 for key derivation.
/// </summary>
public interface IExportEncryptionService
{
    /// <summary>Encrypts a file with the given password. Output path gets .encrypted extension if not already.</summary>
    void EncryptFile(string sourcePath, string destPath, string password);
    /// <summary>Decrypts an encrypted export file to the given path.</summary>
    void DecryptFile(string encryptedPath, string destPath, string password);
}

public class ExportEncryptionService : IExportEncryptionService
{
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly byte[] FileHeader = { 0x57, 0x41, 0x45, 0x58 }; // "WAEX" WorkAudit Encrypted eXport

    private readonly ILogger _log = LoggingService.ForContext<ExportEncryptionService>();

    public void EncryptFile(string sourcePath, string destPath, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required for encryption.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = DeriveKey(password, salt);

        var outPath = destPath.EndsWith(".encrypted", StringComparison.OrdinalIgnoreCase) ? destPath : destPath + ".encrypted";

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;

        using (var fsIn = File.OpenRead(sourcePath))
        using (var fsOut = File.Create(outPath))
        using (var encryptor = aes.CreateEncryptor())
        using (var cs = new CryptoStream(fsOut, encryptor, CryptoStreamMode.Write))
        {
            fsOut.Write(FileHeader, 0, FileHeader.Length);
            fsOut.Write(salt, 0, salt.Length);
            fsOut.Write(iv, 0, iv.Length);
            fsIn.CopyTo(cs);
        }

        _log.Information("Encrypted export to {Path}", outPath);
    }

    public void DecryptFile(string encryptedPath, string destPath, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required for decryption.", nameof(password));

        using var fsIn = File.OpenRead(encryptedPath);
        var header = new byte[FileHeader.Length];
        if (fsIn.Read(header, 0, header.Length) != header.Length || !header.SequenceEqual(FileHeader))
            throw new InvalidOperationException("File is not a valid WorkAudit encrypted export.");

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        if (fsIn.Read(salt, 0, salt.Length) != salt.Length || fsIn.Read(iv, 0, iv.Length) != iv.Length)
            throw new InvalidOperationException("Encrypted file is corrupted or truncated.");

        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;

        using (var fsOut = File.Create(destPath))
        using (var decryptor = aes.CreateDecryptor())
        using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
        {
            cs.CopyTo(fsOut);
        }

        _log.Information("Decrypted export to {Path}", destPath);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
