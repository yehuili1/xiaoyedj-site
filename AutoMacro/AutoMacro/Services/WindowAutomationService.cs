using System.Runtime.InteropServices;
using System.Text;
using AutoMacro.Models;

namespace AutoMacro.Services;

public sealed record WindowSnapshot(
    IntPtr Handle,
    string Title,
    string ProcessName,
    int Left,
    int Top,
    int Width,
    int Height,
    int ClientLeft,
    int ClientTop,
    int ClientWidth,
    int ClientHeight);

public class WindowAutomationService : IWindowAutomationService
{
    private readonly IRunLogger _logger;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    public WindowAutomationService(IRunLogger logger)
    {
        _logger = logger;
    }

    public WindowSnapshot? GetForegroundWindowSnapshot()
    {
        var handle = GetForegroundWindow();
        return handle == IntPtr.Zero ? null : CreateSnapshot(handle);
    }

    public bool TryResolvePoint(InputEvent evt, out int x, out int y)
    {
        x = evt.X;
        y = evt.Y;

        if (!evt.UseWindowRelativeCoordinates || string.IsNullOrWhiteSpace(evt.WindowTitle))
            return false;

        var snapshot = FindWindow(evt.WindowTitle, evt.WindowProcessName, allowProcessFallback: false)
                       ?? FindWindow(null, evt.WindowProcessName, allowProcessFallback: true, requireUniqueProcessFallback: true);
        if (snapshot is null)
        {
            _logger.Warn("Window", $"未找到唯一录制窗口，使用绝对坐标: title=\"{evt.WindowTitle}\", process=\"{evt.WindowProcessName}\", ({evt.X}, {evt.Y})");
            return false;
        }

        if (evt.UseWindowClientCoordinates &&
            evt.RecordedClientWidth > 0 &&
            evt.RecordedClientHeight > 0 &&
            snapshot.ClientWidth > 0 &&
            snapshot.ClientHeight > 0)
        {
            x = snapshot.ClientLeft + ResolveHorizontalCoordinate(evt.ClientRelativeX, evt.RecordedClientWidth, snapshot.ClientWidth);
            y = snapshot.ClientTop + ResolveVerticalCoordinate(evt.ClientRelativeY, evt.RecordedClientHeight, snapshot.ClientHeight);
            return true;
        }

        if (evt.RecordedWindowWidth > 0 &&
            evt.RecordedWindowHeight > 0 &&
            snapshot.Width > 0 &&
            snapshot.Height > 0)
        {
            x = snapshot.Left + ResolveHorizontalCoordinate(evt.RelativeX, evt.RecordedWindowWidth, snapshot.Width);
            y = snapshot.Top + ResolveVerticalCoordinate(evt.RelativeY, evt.RecordedWindowHeight, snapshot.Height);
            return true;
        }

        x = snapshot.Left + evt.RelativeX;
        y = snapshot.Top + evt.RelativeY;
        return true;
    }

    public bool ActivateWindow(string titleKeyword)
    {
        var snapshot = FindWindow(titleKeyword);
        if (snapshot is null)
        {
            _logger.Warn("Window", $"激活窗口失败，未找到: {titleKeyword}");
            return false;
        }

        // A small Alt tap helps Windows allow foreground changes from automation processes.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        var ok = SetForegroundWindow(snapshot.Handle);
        _logger.Info("Window", $"激活窗口: ok={ok}, title=\"{snapshot.Title}\"");
        return ok;
    }

    public async Task<bool> WaitForWindowAsync(string titleKeyword, int timeoutMs, CancellationToken cancellationToken)
    {
        var endAt = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindWindow(titleKeyword) is not null)
            {
                _logger.Info("Window", $"等待窗口成功: {titleKeyword}");
                return true;
            }

            await Task.Delay(200, cancellationToken);
        } while (DateTime.UtcNow < endAt);

        _logger.Error("Window", $"等待窗口超时: {titleKeyword}", captureScreenshot: true);
        return false;
    }

    private WindowSnapshot? FindWindow(
        string? titleKeyword,
        string? processName = null,
        bool allowProcessFallback = true,
        bool requireUniqueProcessFallback = false)
    {
        var exactTitleMatches = new List<WindowSnapshot>();
        var stableTitleMatches = new List<WindowSnapshot>();
        var processMatches = new List<WindowSnapshot>();
        var keyword = titleKeyword?.Trim() ?? "";
        var processKeyword = processName?.Trim() ?? "";
        var stableKeyword = GetStableTitleKeyword(keyword);
        if (string.IsNullOrWhiteSpace(keyword) && string.IsNullOrWhiteSpace(processKeyword))
            return null;

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            var snapshot = CreateSnapshot(handle);
            if (snapshot is null || !IsUsableWindow(snapshot))
                return true;

            if (!string.IsNullOrWhiteSpace(keyword) &&
                snapshot.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                exactTitleMatches.Add(snapshot);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(stableKeyword) &&
                snapshot.Title.Contains(stableKeyword, StringComparison.OrdinalIgnoreCase))
            {
                stableTitleMatches.Add(snapshot);
                return true;
            }

            if (allowProcessFallback &&
                !string.IsNullOrWhiteSpace(processKeyword) &&
                snapshot.ProcessName.Equals(processKeyword, StringComparison.OrdinalIgnoreCase))
            {
                processMatches.Add(snapshot);
            }

            return true;
        }, IntPtr.Zero);

        var exactTitleMatch = ChooseWindowMatch(exactTitleMatches, processKeyword, requireUnique: false);
        if (exactTitleMatch is not null)
            return exactTitleMatch;

        var stableTitleMatch = ChooseWindowMatch(stableTitleMatches, processKeyword, requireUnique: true);
        if (stableTitleMatch is not null)
            return stableTitleMatch;

        if (!allowProcessFallback || processMatches.Count == 0)
            return null;

        return ChooseWindowMatch(processMatches, processKeyword, requireUniqueProcessFallback);
    }

    private static WindowSnapshot? ChooseWindowMatch(
        IReadOnlyList<WindowSnapshot> matches,
        string processKeyword,
        bool requireUnique)
    {
        if (matches.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(processKeyword))
        {
            var processMatches = matches
                .Where(m => m.ProcessName.Equals(processKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (processMatches.Count == 1)
                return processMatches[0];

            if (processMatches.Count > 1)
                return requireUnique ? null : processMatches[0];
        }

        if (matches.Count == 1)
            return matches[0];

        return requireUnique ? null : matches[0];
    }

    private static bool IsUsableWindow(WindowSnapshot snapshot)
    {
        return snapshot.Left > -30000 &&
               snapshot.Top > -30000 &&
               snapshot.Width >= 80 &&
               snapshot.Height >= 80;
    }

    private static string GetStableTitleKeyword(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var end = trimmed.Length;
        while (end > 0 && char.IsDigit(trimmed[end - 1]))
            end--;

        var stable = trimmed[..end].Trim();
        return stable.Length >= 2 ? stable : string.Empty;
    }

    private static WindowSnapshot? CreateSnapshot(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
            return null;

        var title = GetTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var processName = "";
        try
        {
            GetWindowThreadProcessId(handle, out var processId);
            processName = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
        }

        var clientLeft = rect.Left;
        var clientTop = rect.Top;
        var clientWidth = Math.Max(0, rect.Right - rect.Left);
        var clientHeight = Math.Max(0, rect.Bottom - rect.Top);
        if (GetClientRect(handle, out var clientRect))
        {
            var clientOrigin = new Point();
            if (ClientToScreen(handle, ref clientOrigin))
            {
                clientLeft = clientOrigin.X;
                clientTop = clientOrigin.Y;
                clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
                clientHeight = Math.Max(0, clientRect.Bottom - clientRect.Top);
            }
        }

        return new WindowSnapshot(
            handle,
            title,
            processName,
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top),
            clientLeft,
            clientTop,
            clientWidth,
            clientHeight);
    }

    private static int ResolveHorizontalCoordinate(int recordedCoordinate, int recordedSize, int currentSize)
    {
        if (recordedSize <= 0)
            return recordedCoordinate;

        var startDistance = recordedCoordinate;
        var endDistance = recordedSize - recordedCoordinate;
        var edgeBand = GetEdgeBand(recordedSize);

        if (startDistance <= edgeBand && endDistance > edgeBand)
            return ClampCoordinate(startDistance, currentSize);

        if (endDistance <= edgeBand && startDistance > edgeBand)
            return ClampCoordinate(currentSize - endDistance, currentSize);

        var ratio = recordedCoordinate / (double)recordedSize;
        return ClampCoordinate((int)Math.Round(Math.Clamp(ratio, 0, 1) * currentSize), currentSize);
    }

    private static int ResolveVerticalCoordinate(int recordedCoordinate, int recordedSize, int currentSize)
    {
        if (recordedSize <= 0)
            return recordedCoordinate;

        var startDistance = recordedCoordinate;
        var endDistance = recordedSize - recordedCoordinate;
        var edgeBand = GetEdgeBand(recordedSize);

        if (startDistance <= edgeBand && endDistance > edgeBand)
            return ClampCoordinate(startDistance, currentSize);

        if (endDistance <= edgeBand && startDistance > edgeBand)
            return ClampCoordinate(currentSize - endDistance, currentSize);

        var ratio = recordedCoordinate / (double)recordedSize;
        return ClampCoordinate((int)Math.Round(Math.Clamp(ratio, 0, 1) * currentSize), currentSize);
    }

    private static int GetEdgeBand(int recordedSize)
    {
        return (int)Math.Round(Math.Clamp(recordedSize * 0.28, 100, 320));
    }

    private static int ClampCoordinate(int coordinate, int currentSize)
    {
        return Math.Clamp(coordinate, 0, Math.Max(0, currentSize));
    }

    private static string GetTitle(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }
}
