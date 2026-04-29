using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public sealed class ShellConfiguration
{
    public bool CanAccessDashboard { get; init; }
    public bool IsManagerOrAdmin { get; init; }
}

public interface IShellPolicyService
{
    ShellConfiguration BuildForRole(string? role);
}

public sealed class ShellPolicyService : IShellPolicyService
{
    public ShellConfiguration BuildForRole(string? role)
    {
        var roleLevel = Roles.GetRoleLevel(role ?? Roles.Viewer);
        return new ShellConfiguration
        {
            CanAccessDashboard = roleLevel >= 2,
            IsManagerOrAdmin = roleLevel >= 4
        };
    }
}

public interface ILocalizationApplier
{
    string Get(IConfigStore config, string key, params object[] args);
}

public sealed class LocalizationApplier : ILocalizationApplier
{
    public string Get(IConfigStore config, string key, params object[] args) =>
        ReportLocalizationService.GetString(key, config, args);
}
