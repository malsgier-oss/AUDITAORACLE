using WorkAudit.Core.Services;
using Xunit;

namespace WorkAudit.Tests.Core.Services;

public class ShellNavigationServiceTests
{
    [Fact]
    public void TryActivateView_WithValidIndex_InvokesCallback()
    {
        var service = new ShellNavigationService();
        var activatedIndex = -1;

        var ok = service.TryActivateView(2, 5, idx => activatedIndex = idx);

        Assert.True(ok);
        Assert.Equal(2, activatedIndex);
    }

    [Fact]
    public void TryActivateView_WithInvalidIndex_DoesNotInvokeCallback()
    {
        var service = new ShellNavigationService();
        var activated = false;

        var ok = service.TryActivateView(9, 5, _ => activated = true);

        Assert.False(ok);
        Assert.False(activated);
    }
}
