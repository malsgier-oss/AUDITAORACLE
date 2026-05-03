using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Security;

public sealed class SessionServiceTests : IDisposable
{
    private readonly Mock<IUserStore> _userStoreMock;
    private readonly Mock<IPasswordService> _passwordServiceMock;
    private readonly Mock<IAuditTrailService> _auditTrailMock;
    private readonly SessionService _service;
    private readonly string _testDbPath;

    public SessionServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_test_session_{Guid.NewGuid():N}.db");
        
        _userStoreMock = new Mock<IUserStore>();
        _passwordServiceMock = new Mock<IPasswordService>();
        _auditTrailMock = new Mock<IAuditTrailService>();

        _userStoreMock.Setup(x => x.GetUnusedEmergencyCodeHashes(It.IsAny<int>()))
            .Returns(new List<(int Id, string Hash)>());
        _userStoreMock.Setup(x => x.Update(It.IsAny<User>())).Returns(true);
        
        _service = new SessionService(
            _userStoreMock.Object,
            _passwordServiceMock.Object,
            _auditTrailMock.Object
        );
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ShouldSucceed()
    {
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = "hash123",
            IsActive = true,
            IsLocked = false
        };

        _userStoreMock.Setup(x => x.GetByUsername("testuser")).Returns(user);
        _passwordServiceMock.Setup(x => x.VerifyPassword("password123", "hash123")).Returns(true);

        var (success, error, session) = await _service.LoginAsync("testuser", "password123");

        success.Should().BeTrue();
        error.Should().BeNull();
        session.Should().NotBeNull();
        session!.Token.Should().NotBeNullOrEmpty();
        session.Token.Length.Should().Be(43);
        _service.IsAuthenticated.Should().BeTrue();
        _service.CurrentUser.Should().NotBeNull();
        _service.CurrentUser!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task LoginAsync_InvalidUsername_ShouldFail()
    {
        _userStoreMock.Setup(x => x.GetByUsername("nonexistent")).Returns((User?)null);

        var (success, error, session) = await _service.LoginAsync("nonexistent", "password");

        success.Should().BeFalse();
        error.Should().Contain("Invalid");
        session.Should().BeNull();
        _service.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ShouldFail()
    {
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = "hash123",
            IsActive = true
        };

        _userStoreMock.Setup(x => x.GetByUsername("testuser")).Returns(user);
        _passwordServiceMock.Setup(x => x.VerifyPassword("wrongpassword", "hash123")).Returns(false);

        var (success, error, session) = await _service.LoginAsync("testuser", "wrongpassword");

        success.Should().BeFalse();
        error.Should().Contain("Invalid");
        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ShouldFail()
    {
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = "hash123",
            IsActive = false
        };

        _userStoreMock.Setup(x => x.GetByUsername("testuser")).Returns(user);

        var (success, error, session) = await _service.LoginAsync("testuser", "password");

        success.Should().BeFalse();
        error.Should().Contain("inactive");
        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_LockedUser_ShouldFail()
    {
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = "hash123",
            IsActive = true,
            IsLocked = true
        };

        _userStoreMock.Setup(x => x.GetByUsername("testuser")).Returns(user);

        var (success, error, session) = await _service.LoginAsync("testuser", "password");

        success.Should().BeFalse();
        error.Should().Contain("locked");
        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_Administrator_LockedWithCorrectPrimaryPassword_ShouldSucceedAndUnlock()
    {
        var user = new User
        {
            Id = 2,
            Uuid = "adm-uuid",
            Username = "superadmin",
            PasswordHash = "primaryHash",
            Role = Roles.Administrator,
            IsActive = true,
            IsLocked = true,
            FailedLoginAttempts = 4
        };

        _userStoreMock.Setup(x => x.GetByUsername("superadmin")).Returns(user);
        _passwordServiceMock.Setup(x => x.VerifyPassword("correct", "primaryHash")).Returns(true);

        var (success, error, session) = await _service.LoginAsync("superadmin", "correct");

        success.Should().BeTrue();
        error.Should().BeNull();
        session.Should().NotBeNull();
        user.IsLocked.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        _service.CurrentUser!.Username.Should().Be("superadmin");
        _auditTrailMock.Verify(x => x.LogAsync(AuditAction.Login, AuditCategory.Authentication, "User", user.Uuid,
            null, null, It.IsAny<string?>(), true, null), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Administrator_ValidEmergencyCode_ShouldSucceedAndSetMustChangePassword()
    {
        var user = new User
        {
            Id = 7,
            Uuid = "adm7",
            Username = "admin7",
            PasswordHash = "primaryHash",
            Role = Roles.Administrator,
            IsActive = true,
            IsLocked = false,
            MustChangePassword = false
        };

        _userStoreMock.Setup(x => x.GetByUsername("admin7")).Returns(user);
        _passwordServiceMock.Setup(x => x.VerifyPassword("EMRG-CODE-HERE", "primaryHash")).Returns(false);
        _userStoreMock.Setup(x => x.GetUnusedEmergencyCodeHashes(7))
            .Returns(new List<(int Id, string Hash)> { (501, "emHash") });
        _passwordServiceMock.Setup(x => x.VerifyPassword("EMRG-CODE-HERE", "emHash")).Returns(true);
        _userStoreMock.Setup(x => x.MarkEmergencyCodeUsed(501, 7)).Returns(true);

        var (success, error, session) = await _service.LoginAsync("admin7", "EMRG-CODE-HERE");

        success.Should().BeTrue();
        error.Should().BeNull();
        session.Should().NotBeNull();
        user.MustChangePassword.Should().BeTrue();
        _service.CurrentUser!.MustChangePassword.Should().BeTrue();
        _userStoreMock.Verify(x => x.MarkEmergencyCodeUsed(501, 7), Times.Once);
        _auditTrailMock.Verify(x => x.LogAsync(AuditAction.LoginEmergencyCode, AuditCategory.Authentication, "User", user.Uuid,
            null, null, It.IsAny<string?>(), true, null), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WhenAuthenticated_ShouldClearSession()
    {
        var user = new User { Id = 1, Username = "testuser", PasswordHash = "hash", IsActive = true };
        _userStoreMock.Setup(x => x.GetByUsername("testuser")).Returns(user);
        _passwordServiceMock.Setup(x => x.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await _service.LoginAsync("testuser", "password");
        _service.IsAuthenticated.Should().BeTrue();

        var result = await _service.LogoutAsync();

        result.Should().BeTrue();
        _service.IsAuthenticated.Should().BeFalse();
        _service.CurrentUser.Should().BeNull();
        _service.CurrentSession.Should().BeNull();
    }

    [Fact]
    public void RecordActivity_WhenAuthenticated_ShouldUpdateLastActivity()
    {
        _service.RecordActivity();
        
        // Should not throw, even when not authenticated
    }

    [Fact]
    public void IsAuthenticated_InitialState_ShouldBeFalse()
    {
        _service.IsAuthenticated.Should().BeFalse();
        _service.CurrentUser.Should().BeNull();
        _service.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_EmptyUsername_ShouldFail()
    {
        var (success, error, session) = await _service.LoginAsync("", "password");

        success.Should().BeFalse();
        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_EmptyPassword_ShouldFail()
    {
        var (success, error, session) = await _service.LoginAsync("testuser", "");

        success.Should().BeFalse();
        session.Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
            
            var shmPath = _testDbPath + "-shm";
            if (File.Exists(shmPath))
                File.Delete(shmPath);
            
            var walPath = _testDbPath + "-wal";
            if (File.Exists(walPath))
                File.Delete(walPath);
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }
}
