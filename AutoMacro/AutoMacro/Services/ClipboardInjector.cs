using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoMacro.Services;

public class ClipboardInjector : IClipboardInjector
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int ClipboardWriteTimeoutMs = 15000;
    private const int ClipboardRetryDelayMs = 75;

    private readonly IRunLogger _logger;

    public ClipboardInjector(IRunLogger logger)
    {
        _logger = logger;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    public async Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _logger.Info("Clipboard", $"准备粘贴，文本长度={text.Length}，前台窗口=\"{GetForegroundWindowTitle()}\"");

        await SetClipboardTextAsync(text, cancellationToken);
        _logger.Info("Clipboard", $"剪贴板写入成功，开始模拟 Ctrl+V，前台窗口=\"{GetForegroundWindowTitle()}\"");

        await Task.Delay(20, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SendCtrlV();
        await Task.Delay(50, cancellationToken);

        _logger.Info("Clipboard", $"Ctrl+V 已发送，当前前台窗口=\"{GetForegroundWindowTitle()}\"");
    }

    public async Task CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        await SetClipboardTextAsync(text, cancellationToken);
        _logger.Info("Clipboard", $"已复制到剪贴板，文本长度={text.Length}");
    }

    private async Task SetClipboardTextAsync(string text, CancellationToken cancellationToken)
    {
        var normalizedText = NormalizeClipboardText(text);
        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        var lastError = "";
        var lastOwner = "";

        while (stopwatch.ElapsedMilliseconds < ClipboardWriteTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            if (TrySetClipboardText(normalizedText, out lastError))
            {
                if (attempt > 1)
                    _logger.Info("Clipboard", $"剪贴板第 {attempt} 次写入成功");
                return;
            }

            lastOwner = GetOpenClipboardOwnerText();
            if (attempt == 1 || attempt % 10 == 0)
            {
                _logger.Warn("Clipboard",
                    $"剪贴板暂时被占用，第 {attempt} 次，错误={lastError}，占用={lastOwner}");
            }

            await Task.Delay(ClipboardRetryDelayMs, cancellationToken);
        }

        throw new InvalidOperationException(
            $"剪贴板持续被占用 {ClipboardWriteTimeoutMs / 1000} 秒，无法粘贴。最后错误={lastError}，占用={lastOwner}");
    }

    private static bool TrySetClipboardText(string text, out string error)
    {
        error = "";

        if (!OpenClipboard(IntPtr.Zero))
        {
            error = $"OpenClipboard Win32Error={Marshal.GetLastWin32Error()}";
            return false;
        }

        IntPtr hGlobal = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                error = $"EmptyClipboard Win32Error={Marshal.GetLastWin32Error()}";
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero)
            {
                error = $"GlobalAlloc Win32Error={Marshal.GetLastWin32Error()}";
                return false;
            }

            var target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
            {
                error = $"GlobalLock Win32Error={Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                error = $"SetClipboardData Win32Error={Marshal.GetLastWin32Error()}";
                return false;
            }

            hGlobal = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (hGlobal != IntPtr.Zero)
                GlobalFree(hGlobal);
            CloseClipboard();
        }
    }

    private static void SendCtrlV()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static string NormalizeClipboardText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static string GetForegroundWindowTitle()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "";

            return GetWindowTitle(handle);
        }
        catch
        {
            return "";
        }
    }

    private static string GetOpenClipboardOwnerText()
    {
        try
        {
            var handle = GetOpenClipboardWindow();
            if (handle == IntPtr.Zero)
                return "未返回占用窗口";

            GetWindowThreadProcessId(handle, out var processId);
            var processName = "";
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
            }

            var title = GetWindowTitle(handle);
            return $"pid={processId}, process={processName}, window=\"{title}\"";
        }
        catch (Exception ex)
        {
            return $"查询占用者失败: {ex.Message}";
        }
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }
}
