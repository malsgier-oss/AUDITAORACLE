using System;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Security;

public class PermissionServiceTests
{
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly Mock<IConfigStore> _configStoreMock;
    private readonly Mock<IDocumentAssignmentStore> _assignmentStoreMock;
    private readonly PermissionService _service;

    public PermissionServiceTests()
    {
        _sessionServiceMock = new Mock<ISessionService>();
        _configStoreMock = new Mock<IConfigStore>();
        _assignmentStoreMock = new Mock<IDocumentAssignmentStore>();
        _assignmentStoreMock.Setup(x => x.ListByDocument(It.IsAny<int>())).Returns([]);
        _configStoreMock.Setup(x => x.GetSettingInt(It.IsAny<string>(), It.IsAny<int>())).Returns(30);
        _service = new PermissionService(_sessionServiceMock.Object, _configStoreMock.Object, _assignmentStoreMock.Object);
    }

    [Fact]
    public void HasPermission_NotAuthenticated_ShouldReturnFalse()
    {
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(false);

        var result = _service.HasPermission(Permissions.DocumentCreate);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_Administrator_ShouldHaveAllPermissions()
    {
        var admin = new User { Role = Roles.Administrator };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);

        var result1 = _service.HasPermission(Permissions.DocumentCreate);
        var result2 = _service.HasPermission(Permissions.UserCreate);
        var result3 = _service.HasPermission(Permissions.SettingsEdit);

        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
    }

    [Fact]
    public void HasPermission_Viewer_ShouldOnlyViewDocuments()
    {
        var viewer = new User { Role = Roles.Viewer };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(viewer);

        var canView = _service.HasPermission(Permissions.DocumentView);
        var canCreate = _service.HasPermission(Permissions.DocumentCreate);
        var canDelete = _service.HasPermission(Permissions.DocumentDelete);

        canView.Should().BeTrue();
        canCreate.Should().BeFalse();
        canDelete.Should().BeFalse();
    }

    [Fact]
    public void HasMinimumRole_Administrator_ShouldHaveAllRoles()
    {
        var admin = new User { Role = Roles.Administrator };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);

        var hasAdmin = _service.HasMinimumRole(Roles.Administrator);
        var hasManager = _service.HasMinimumRole(Roles.Manager);
        var hasAuditor = _service.HasMinimumRole(Roles.Auditor);
        var hasViewer = _service.HasMinimumRole(Roles.Viewer);

        hasAdmin.Should().BeTrue();
        hasManager.Should().BeTrue();
        hasAuditor.Should().BeTrue();
        hasViewer.Should().BeTrue();
    }

    [Fact]
    public void HasMinimumRole_Viewer_ShouldOnlyHaveViewerRole()
    {
        var viewer = new User { Role = Roles.Viewer };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(viewer);

        var hasAdmin = _service.HasMinimumRole(Roles.Administrator);
        var hasManager = _service.HasMinimumRole(Roles.Manager);
        var hasAuditor = _service.HasMinimumRole(Roles.Auditor);
        var hasViewer = _service.HasMinimumRole(Roles.Viewer);

        hasAdmin.Should().BeFalse();
        hasManager.Should().BeFalse();
        hasAuditor.Should().BeFalse();
        hasViewer.Should().BeTrue();
    }

    [Fact]
    public void CanAccessDocument_NotAuthenticated_ShouldReturnFalse()
    {
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(false);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanAccessDocument_SameBranch_ShouldReturnTrue()
    {
        var user = new User { Role = Roles.Manager, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(user);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanAccessDocument_DifferentBranch_NonAdmin_ShouldReturnTrue()
    {
        var manager = new User { Role = Roles.Manager, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(manager);
        var doc = new Document { Branch = "Branch2" };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanAccessDocument_DifferentBranch_Administrator_ShouldReturnTrue()
    {
        var admin = new User { Role = Roles.Administrator, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);
        var doc = new Document { Branch = "Branch2" };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanAccessDocument_Auditor_DifferentBranch_ShouldReturnFalse()
    {
        var auditor = new User { Id = 1, Role = Roles.Auditor, Branch = Branches.MainBranch };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(auditor);
        var doc = new Document { Id = 100, Branch = Branches.MisrataBranch };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanAccessDocument_Auditor_DifferentBranch_ActiveAssignment_ShouldReturnTrue()
    {
        var auditor = new User { Id = 1, Role = Roles.Auditor, Branch = Branches.MainBranch };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(auditor);
        var doc = new Document { Id = 100, Branch = Branches.MisrataBranch, ArchivedAt = null };
        _assignmentStoreMock.Setup(x => x.ListByDocument(100)).Returns(
        [
            new DocumentAssignment
            {
                DocumentId = 100,
                AssignedToUserId = 1,
                Status = AssignmentStatus.Pending
            }
        ]);

        var result = _service.CanAccessDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanAccessDocument_Viewer_AllBranches_DifferentConcreteBranch_ShouldReturnFalse()
    {
        var viewer = new User { Id = 2, Role = Roles.Viewer, Branch = Branches.AllBranchesLabel };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(viewer);
        var doc = new Document { Id = 101, Branch = Branches.MisrataBranch, ArchivedAt = null };

        var result = _service.CanAccessDocument(doc);

        result.Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveDocumentListBranchFilter_Manager_ShouldReturnNull()
    {
        var manager = new User { Role = Roles.Manager, Branch = Branches.MainBranch };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(manager);

        _service.GetEffectiveDocumentListBranchFilter().Should().BeNull();
    }

    [Fact]
    public void GetEffectiveDocumentListBranchFilter_Auditor_ShouldReturnConcreteBranch()
    {
        var auditor = new User { Role = Roles.Auditor, Branch = Branches.MisrataBranch };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(auditor);

        _service.GetEffectiveDocumentListBranchFilter().Should().Be(Branches.MisrataBranch);
    }

    [Fact]
    public void CanEditDocument_Administrator_ShouldReturnTrue()
    {
        var admin = new User { Role = Roles.Administrator, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanEditDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanEditDocument_Viewer_ShouldReturnFalse()
    {
        var viewer = new User { Role = Roles.Viewer, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(viewer);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanEditDocument(doc);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanDeleteDocument_Administrator_ShouldReturnTrue()
    {
        var admin = new User { Role = Roles.Administrator, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanDeleteDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanDeleteDocument_Manager_ShouldReturnTrue()
    {
        var manager = new User { Role = Roles.Manager, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(manager);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanDeleteDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanApproveDocument_Manager_ShouldReturnTrue()
    {
        var manager = new User { Role = Roles.Manager, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(manager);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanApproveDocument(doc);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanApproveDocument_Auditor_ShouldReturnFalse()
    {
        var auditor = new User { Role = Roles.Auditor, Branch = "Branch1" };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(auditor);
        var doc = new Document { Branch = "Branch1" };

        var result = _service.CanApproveDocument(doc);

        result.Should().BeFalse();
    }

    [Fact]
    public void GetCurrentPermissions_Administrator_ShouldReturnAllPermissions()
    {
        var admin = new User { Role = Roles.Administrator };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(admin);

        var permissions = _service.GetCurrentPermissions();

        permissions.Should().NotBeEmpty();
        permissions.Should().Contain(Permissions.DocumentCreate);
        permissions.Should().Contain(Permissions.UserCreate);
        permissions.Should().Contain(Permissions.SettingsEdit);
    }

    [Fact]
    public void GetCurrentPermissions_Viewer_ShouldReturnLimitedPermissions()
    {
        var viewer = new User { Role = Roles.Viewer };
        _sessionServiceMock.Setup(x => x.IsAuthenticated).Returns(true);
        _sessionServiceMock.Setup(x => x.CurrentUser).Returns(viewer);

        var permissions = _service.GetCurrentPermissions();

        permissions.Should().Contain(Permissions.DocumentView);
        permissions.Should().NotContain(Permissions.DocumentCreate);
        permissions.Should().NotContain(Permissions.DocumentDelete);
    }
}
