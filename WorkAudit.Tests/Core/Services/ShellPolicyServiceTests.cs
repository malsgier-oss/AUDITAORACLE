using WorkAudit.Core.Services;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Core.Services;

public class ShellPolicyServiceTests
{
    [Fact]
    public void BuildForRole_Manager_HasDashboardAndManagerAccess()
    {
        var service = new ShellPolicyService();

        var config = service.BuildForRole(Roles.Manager);

        Assert.True(config.CanAccessDashboard);
        Assert.True(config.IsManagerOrAdmin);
    }

    [Fact]
    public void BuildForRole_Auditor_HasDashboardButNotManagerAccess()
    {
        var service = new ShellPolicyService();

        var config = service.BuildForRole(Roles.Auditor);

        Assert.True(config.CanAccessDashboard);
        Assert.False(config.IsManagerOrAdmin);
    }

    [Fact]
    public void BuildForRole_Viewer_HasNoDashboardAccess()
    {
        var service = new ShellPolicyService();

        var config = service.BuildForRole(Roles.Viewer);

        Assert.False(config.CanAccessDashboard);
        Assert.False(config.IsManagerOrAdmin);
    }
}
