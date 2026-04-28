using System;
using System.IO;
using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Security;

/// <summary>
/// Database file encryption service using Windows DPAPI and AES-256.
/// Provides file-level encryption for SQLite database files to protect sensitive data at rest.
/// Uses machine-specific encryption keys, ensuring the database can only be decrypted on the same machine.
/// </summary>
public interface IDatabaseEncryptionService
{
    /// <summary>Encrypts a SQLite database file. Creates .encrypted version.</summary>
    void EncryptDatabaseFile(string dbPath);
    
    /// <summary>Decrypts an encrypted database file. Restores to .db format.</summary>
    void DecryptDatabaseFile(string encryptedDbPath, string outputDbPath);
    
    /// <summary>Checks if a database file is encrypted.</summary>
    bool IsEncrypted(string dbPath);
}

/// <summary>
/// Implementation using Windows DPAPI for machine-specific key protection and AES-256 for file encryption.
/// NOTE: This is file-level encryption. For full database encryption at rest, consider SQLCipher.
/// For bank deployment: Configure Windows disk encryption (BitLocker) as the primary defense.
/// This service provides additional application-layer protection for offline database files.
/// </summary>
public class DatabaseEncryptionService : IDatabaseEncryptionService
{
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly byte[] FileHeader = { 0x57, 0x41, 0x44, 0x42 }; // "WADB" WorkAudit DataBase
    
    private readonly ILogger _log = LoggingService.ForContext<DatabaseEncryptionService>();
    private readonly byte[] _machineKey;

    public DatabaseEncryptionService()
    {
        _machineKey = GetOrCreateMachineKey();
    }

    public void EncryptDatabaseFile(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database file not found", dbPath);

        if (IsEncrypted(dbPath))
        {
            _log.Warning("Database file is already encrypted: {Path}", dbPath);
            return;
        }

        var encryptedPath = dbPath + ".encrypted";
        var tempPath = dbPath + ".encrypting";

        try
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var key = DeriveKey(salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var fsIn = File.OpenRead(dbPath))
            using (var fsOut = File.Create(tempPath))
            using (var encryptor = aes.CreateEncryptor())
            using (var cs = new CryptoStream(fsOut, encryptor, CryptoStreamMode.Write))
            {
                fsOut.Write(FileHeader, 0, FileHeader.Length);
                fsOut.Write(salt, 0, salt.Length);
                fsOut.Write(iv, 0, iv.Length);
                fsIn.CopyTo(cs);
            }

            File.Move(tempPath, encryptedPath, overwrite: true);
            _log.Information("Database file encrypted: {Path} -> {EncryptedPath}", dbPath, encryptedPath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            
            _log.Error(ex, "Failed to encrypt database file");
            throw;
        }
    }

    public void DecryptDatabaseFile(string encryptedDbPath, string outputDbPath)
    {
        if (!File.Exists(encryptedDbPath))
            throw new FileNotFoundException("Encrypted database file not found", encryptedDbPath);

        if (!IsEncrypted(encryptedDbPath))
            throw new InvalidOperationException("File is not an encrypted WorkAudit database");

        var tempPath = outputDbPath + ".decrypting";

        try
        {
            using var fsIn = File.OpenRead(encryptedDbPath);
            
            var header = new byte[FileHeader.Length];
            if (fsIn.Read(header, 0, header.Length) != header.Length || !header.SequenceEqual(FileHeader))
                throw new InvalidOperationException("File is not a valid WorkAudit encrypted database");

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            if (fsIn.Read(salt, 0, salt.Length) != salt.Length || fsIn.Read(iv, 0, iv.Length) != iv.Length)
                throw new InvalidOperationException("Encrypted database file is corrupted or truncated");

            var key = DeriveKey(salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var fsOut = File.Create(tempPath))
            using (var decryptor = aes.CreateDecryptor())
            using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
            {
                cs.CopyTo(fsOut);
            }

            File.Move(tempPath, outputDbPath, overwrite: true);
            _log.Information("Database file decrypted: {EncryptedPath} -> {Path}", encryptedDbPath, outputDbPath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            
            _log.Error(ex, "Failed to decrypt database file");
            throw;
        }
    }

    public bool IsEncrypted(string dbPath)
    {
        if (!File.Exists(dbPath))
            return false;

        try
        {
            using var fs = File.OpenRead(dbPath);
            if (fs.Length < FileHeader.Length)
                return false;

            var header = new byte[FileHeader.Length];
            fs.Read(header, 0, header.Length);
            return header.SequenceEqual(FileHeader);
        }
        catch
        {
            return false;
        }
    }

    private byte[] DeriveKey(byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(_machineKey, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    private byte[] GetOrCreateMachineKey()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appDataPath, "WORKAUDIT");
        Directory.CreateDirectory(configDir);
        var keyPath = Path.Combine(configDir, ".dbkey");

        byte[] key;

        if (File.Exists(keyPath))
        {
            var encryptedKey = File.ReadAllBytes(keyPath);
            key = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.LocalMachine);
        }
        else
        {
            key = RandomNumberGenerator.GetBytes(64);
            var encryptedKey = ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(keyPath, encryptedKey);
            _log.Information("Created new machine-specific database encryption key");
        }

        return key;
    }
}
