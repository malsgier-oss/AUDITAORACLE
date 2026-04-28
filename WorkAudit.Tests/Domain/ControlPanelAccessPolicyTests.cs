using FluentAssertions;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Domain;

/// <summary>
/// Documents the role gate for full Control Panel vs preferences-only (see ControlPanelWindow).
/// </summary>
public class ControlPanelAccessPolicyTests
{
    [Theory]
    [InlineData(Roles.Viewer, false)]
    [InlineData(Roles.Auditor, false)]
    [InlineData(Roles.Reviewer, false)]
    [InlineData(Roles.Manager, true)]
    [InlineData(Roles.Administrator, true)]
    public void Full_control_panel_requires_manager_role(string role, bool expectedFullPanel)
    {
        Roles.HasMinimumRole(role, Roles.Manager).Should().Be(expectedFullPanel);
    }
}
