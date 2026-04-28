using WorkAudit.Domain;

namespace WorkAudit.Core.Security;

/// <summary>
/// Validates permissions before operations. Use for API/operation-level authorization.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Throws UnauthorizedAccessException if the current user lacks the required permission.
    /// </summary>
    void RequirePermission(string permission);

    /// <summary>
    /// Throws UnauthorizedAccessException if the current user lacks the minimum role.
    /// </summary>
    void RequireRole(string minimumRole);

    /// <summary>
    /// Returns true if the operation is allowed; does not throw.
    /// </summary>
    bool CanPerform(string permission);
}

public class AuthorizationService : IAuthorizationService
{
    private readonly IPermissionService _permissionService;

    public AuthorizationService(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public void RequirePermission(string permission)
    {
        if (!_permissionService.HasPermission(permission))
            throw new UnauthorizedAccessException($"Permission denied: {permission}");
    }

    public void RequireRole(string minimumRole)
    {
        if (!_permissionService.HasMinimumRole(minimumRole))
            throw new UnauthorizedAccessException($"Role insufficient: {minimumRole} required");
    }

    public bool CanPerform(string permission)
    {
        return _permissionService.HasPermission(permission);
    }
}
