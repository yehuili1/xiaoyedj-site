using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IWindowAutomationService
{
    WindowSnapshot? GetForegroundWindowSnapshot();
    bool TryResolvePoint(InputEvent evt, out int x, out int y);
    bool ActivateWindow(string titleKeyword);
    Task<bool> WaitForWindowAsync(string titleKeyword, int timeoutMs, CancellationToken cancellationToken);
}
