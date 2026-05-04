using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Security;

/// <summary>
/// AES-256 encryption for sensitive configuration values (SMTP passwords, API keys, etc.)
/// Uses machine-specific key from Windows DPAPI for encryption key protection.
/// </summary>
public interface ISecureConfigService
{
    /// <summary>Encrypts a plaintext value. Returns base64-encoded encrypted data with embedded salt and IV.</summary>
    string Encrypt(string plaintext);
    
    /// <summary>Decrypts an encrypted value. Returns plaintext or null if decryption fails or value is null/empty.</summary>
    string? Decrypt(string? encryptedValue);
    
    /// <summary>Checks if a value is encrypted (starts with secure prefix).</summary>
    bool IsEncrypted(string? value);
}

public class SecureConfigService : ISecureConfigService
{
    private const string EncryptedPrefix = "enc:v1:";
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    
    private readonly ILogger _log = LoggingService.ForContext<SecureConfigService>();
    private readonly byte[] _machineKey;

    public SecureConfigService()
    {
        _machineKey = GetOrCreateMachineKey();
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

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

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[salt.Length + iv.Length + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
            Buffer.BlockCopy(iv, 0, result, salt.Length, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, result, salt.Length + iv.Length, ciphertext.Length);

            return EncryptedPrefix + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Encryption failed");
            throw new CryptographicException("Failed to encrypt configuration value", ex);
        }
    }

    public string? Decrypt(string? encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue))
            return null;

        if (!encryptedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return encryptedValue;

        try
        {
            var base64Data = encryptedValue.Substring(EncryptedPrefix.Length);
            var data = Convert.FromBase64String(base64Data);

            if (data.Length < SaltSize + IvSize)
                throw new CryptographicException("Encrypted data is too short");

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var ciphertext = new byte[data.Length - SaltSize - IvSize];

            Buffer.BlockCopy(data, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(data, SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(data, SaltSize + IvSize, ciphertext, 0, ciphertext.Length);

            var key = DeriveKey(salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Decryption failed");
            return null;
        }
    }

    public bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
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
        var keyPath = Path.Combine(configDir, ".machinekey");

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
            _log.Information("Created new machine-specific encryption key");
        }

        return key;
    }
}
