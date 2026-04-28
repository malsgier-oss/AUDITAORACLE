using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Security;

/// <summary>
/// Service for secure password hashing and validation.
/// Uses BCrypt for industry-standard security.
/// </summary>
public interface IPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
    bool ValidatePasswordStrength(string password, out List<string> errors);
    string GenerateTemporaryPassword();
    /// <summary>Generates human-readable one-time codes (not subject to interactive password strength rules).</summary>
    IReadOnlyList<string> GenerateEmergencyAccessCodes(int count);
}

public class PasswordService : IPasswordService
{
    private readonly ILogger _log = LoggingService.ForContext<PasswordService>();

    private const int MinLength = 8;
    private const int MaxLength = 128;
    private const int BcryptWorkFactor = 12;

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var hash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
        _log.Debug("Password hashed successfully");
        return hash;
    }

    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            var result = BCrypt.Net.BCrypt.Verify(password, hash);
            _log.Debug("Password verification: {Result}", result ? "Success" : "Failed");
            return result;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Password verification error");
            return false;
        }
    }

    public bool ValidatePasswordStrength(string password, out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return false;
        }

        if (password.Length < MinLength)
        {
            errors.Add($"Password must be at least {MinLength} characters long.");
        }

        if (password.Length > MaxLength)
        {
            errors.Add($"Password must not exceed {MaxLength} characters.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Password must contain at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("Password must contain at least one number.");
        }

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add("Password must contain at least one special character.");
        }

        // Check for common weak patterns
        var lowerPassword = password.ToLower();
        var weakPatterns = new[] { "password", "123456", "qwerty", "admin", "letmein", "welcome" };
        if (weakPatterns.Any(p => lowerPassword.Contains(p)))
        {
            errors.Add("Password contains a common weak pattern.");
        }

        return errors.Count == 0;
    }

    public string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*";

        static int NextInt(int max) => RandomNumberGenerator.GetInt32(max);

        var password = new char[12];

        // Ensure at least one of each type (cryptographically random)
        password[0] = upper[NextInt(upper.Length)];
        password[1] = lower[NextInt(lower.Length)];
        password[2] = digits[NextInt(digits.Length)];
        password[3] = special[NextInt(special.Length)];

        var allChars = upper + lower + digits + special;
        for (int i = 4; i < password.Length; i++)
        {
            password[i] = allChars[NextInt(allChars.Length)];
        }

        // Fisher-Yates shuffle
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    public IReadOnlyList<string> GenerateEmergencyAccessCodes(int count)
    {
        if (count < 1 || count > 50)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 50.");

        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
            list.Add(GenerateOneEmergencyAccessCode());
        return list;
    }

    private static string GenerateOneEmergencyAccessCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        static string Segment(ReadOnlySpan<char> alpha, int len)
        {
            var chars = new char[len];
            for (var i = 0; i < len; i++)
                chars[i] = alpha[RandomNumberGenerator.GetInt32(alpha.Length)];
            return new string(chars);
        }

        return $"{Segment(alphabet, 4)}-{Segment(alphabet, 4)}-{Segment(alphabet, 4)}";
    }
}
