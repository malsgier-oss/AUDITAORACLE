namespace WorkAudit.Core.Security;

/// <summary>
/// Enforces password policy for compliance. Wraps validation and exposes policy description.
/// </summary>
public interface IPasswordPolicyService
{
    int MinimumLength { get; }
    bool RequireUppercase { get; }
    bool RequireLowercase { get; }
    bool RequireDigit { get; }
    bool RequireSpecialChar { get; }
    string GetPolicyDescription();
    bool Validate(string password, out string[] errors);
}

public class PasswordPolicyService : IPasswordPolicyService
{
    private readonly IPasswordService _passwordService;

    public PasswordPolicyService(IPasswordService passwordService)
    {
        _passwordService = passwordService;
    }

    public int MinimumLength => 8;
    public bool RequireUppercase => true;
    public bool RequireLowercase => true;
    public bool RequireDigit => true;
    public bool RequireSpecialChar => true;

    public string GetPolicyDescription()
    {
        var parts = new List<string> { $"At least {MinimumLength} characters" };
        if (RequireUppercase) parts.Add("One uppercase letter");
        if (RequireLowercase) parts.Add("One lowercase letter");
        if (RequireDigit) parts.Add("One number");
        if (RequireSpecialChar) parts.Add("One special character");
        return string.Join(", ", parts);
    }

    public bool Validate(string password, out string[] errors)
    {
        var list = new List<string>();
        var ok = _passwordService.ValidatePasswordStrength(password, out list);
        errors = list.ToArray();
        return ok;
    }
}
