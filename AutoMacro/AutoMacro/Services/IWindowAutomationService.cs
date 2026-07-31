using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IWindowAutomationService
{
    WindowSnapshot? GetForegroundWindowSnapshot();
    WindowSnapshot? GetWindowSnapshotAtPoint(int x, int y);
    bool TryResolvePoint(InputEvent evt, out int x, out int y);
    bool ActivateWindow(string titleKeyword);
    Task<bool> WaitForWindowAsync(string titleKeyword, int timeoutMs, CancellationToken cancellationToken);
}
