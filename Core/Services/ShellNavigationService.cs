using System;

namespace WorkAudit.Core.Services;

public interface IShellNavigationService
{
    bool TryActivateView(int index, int viewCount, Action<int> applyView);
}

public sealed class ShellNavigationService : IShellNavigationService
{
    public bool TryActivateView(int index, int viewCount, Action<int> applyView)
    {
        if (index < 0 || index >= viewCount)
            return false;

        applyView(index);
        return true;
    }
}
