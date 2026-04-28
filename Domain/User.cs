namespace WorkAudit.Domain;

/// <summary>
/// User entity for authentication and authorization.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Uuid { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Auditor;
    public string? Branch { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLoginAttempts { get; set; }
    public string? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? PasswordChangedAt { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CreatedBy { get; set; }
    public string? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// User session for tracking login state.
/// </summary>
public class Session
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string UserRole { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Role definitions for RBAC.
/// </summary>
public static class Roles
{
    public const string Viewer = "Viewer";
    public const string Auditor = "Auditor";
    public const string Reviewer = "Reviewer";
    public const string Manager = "Manager";
    public const string Administrator = "Administrator";

    public static readonly string[] AllRoles = { Viewer, Auditor, Reviewer, Manager, Administrator };

    public static int GetRoleLevel(string role) => role switch
    {
        Viewer => 1,
        Auditor => 2,
        Reviewer => 3,
        Manager => 4,
        Administrator => 5,
        _ => 0
    };

    public static bool HasMinimumRole(string userRole, string requiredRole)
    {
        return GetRoleLevel(userRole) >= GetRoleLevel(requiredRole);
    }
}
