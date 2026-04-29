using Moq;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Security;

/// <summary>
/// Guards RBAC invariants relied on when multiple clients share one Oracle DB (UI must not be the only gate).
/// </summary>
public sealed class PermissionServiceCriticalPathTests
{
    private static PermissionService CreateService(User? user, Mock<IConfigStore>? configMock = null)
    {
        var session = new Mock<ISessionService>();
        session.Setup(s => s.IsAuthenticated).Returns(user != null);
        session.Setup(s => s.CurrentUser).Returns(user);

        var cfg = configMock ?? new Mock<IConfigStore>();
        cfg.Setup(c => c.GetSettingInt(It.IsAny<string>(), It.IsAny<int>())).Returns(30);

        var assignments = new Mock<IDocumentAssignmentStore>();
        assignments.Setup(a => a.ListByDocument(It.IsAny<int>())).Returns([]);

        return new PermissionService(session.Object, cfg.Object, assignments.Object);
    }

    [Fact]
    public void Viewer_CannotEditDocument_EvenIfDocumentIsDraft()
    {
        var user = new User { Id = 1, Username = "v", Role = Roles.Viewer, Branch = Branches.Default, IsActive = true };
        var svc = CreateService(user);
        var doc = new Document { Id = 1, Branch = Branches.Default, Status = Enums.Status.Draft };
        Assert.False(svc.CanEditDocument(doc));
    }

    [Fact]
    public void Auditor_CanEditDraft_InHomeBranch()
    {
        var user = new User { Id = 2, Username = "a", Role = Roles.Auditor, Branch = Branches.Default, IsActive = true };
        var svc = CreateService(user);
        var doc = new Document { Id = 1, Branch = Branches.Default, Status = Enums.Status.Draft };
        Assert.True(svc.CanEditDocument(doc));
    }

    [Fact]
    public void Unauthenticated_CannotAccessDocument()
    {
        var svc = CreateService(null);
        var doc = new Document { Id = 1, Branch = Branches.Default, Status = Enums.Status.Draft };
        Assert.False(svc.CanAccessDocument(doc));
        Assert.False(svc.HasPermission(Permissions.DocumentEdit));
    }

    [Fact]
    public void Manager_HasArchiveDisposePermission()
    {
        var user = new User { Id = 3, Username = "m", Role = Roles.Manager, Branch = Branches.Default, IsActive = true };
        var svc = CreateService(user);
        Assert.True(svc.HasPermission(Permissions.ArchiveDispose));
    }
}
