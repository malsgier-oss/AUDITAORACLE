using FluentAssertions;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Core;

public class DocumentCreatedByTests
{
    [Fact]
    public void ForUser_PrefersUsername()
    {
        DocumentCreatedBy.ForUser(new User { Username = "alice", DisplayName = "Alice A." }).Should().Be("alice");
    }

    [Fact]
    public void ForUser_FallsBackToDisplayName()
    {
        DocumentCreatedBy.ForUser(new User { Username = "", DisplayName = "Bob" }).Should().Be("Bob");
    }

    [Fact]
    public void FromAppConfiguration_UsesCurrentUserName()
    {
        var app = new AppConfiguration { CurrentUserName = "mgr1" };
        DocumentCreatedBy.FromAppConfiguration(app).Should().Be("mgr1");
    }

    [Fact]
    public void FromAppConfiguration_NullOrEmpty_ReturnsNull()
    {
        DocumentCreatedBy.FromAppConfiguration(new AppConfiguration { CurrentUserName = null }).Should().BeNull();
        DocumentCreatedBy.FromAppConfiguration(new AppConfiguration { CurrentUserName = "  " }).Should().BeNull();
    }
}
