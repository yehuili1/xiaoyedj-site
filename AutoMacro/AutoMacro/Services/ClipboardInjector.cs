using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace AutoMacro.Services;

public class ClipboardInjector : IClipboardInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private readonly IRunLogger _logger;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMs = 20;

    public ClipboardInjector(IRunLogger logger)
    {
        _logger = logger;
    }

    public async Task InjectTextAsync(string text)
    {
        _logger.Info("Clipboard", $"准备粘贴，文本长度={text.Length}，前台窗口=\"{GetForegroundWindowTitle()}\"");

        try
        {
            await SetClipboardTextAsync(text);
            _logger.Info("Clipboard", "剪贴板写入成功，开始模拟 Ctrl+V");

            await Task.Delay(10);

            // 用 Win32 keybd_event 模拟 Ctrl+V（比 SharpHook EventSimulator 更可靠）
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            await Task.Delay(8);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            await Task.Delay(10);
            _logger.Info("Clipboard", $"Ctrl+V 已发送，当前前台窗口=\"{GetForegroundWindowTitle()}\"");
        }
        catch (Exception ex)
        {
            _logger.Warn("Clipboard", $"剪贴板粘贴失败，改用直接输入兜底: {ex.Message}");
            await TypeTextDirectlyAsync(text);
        }
    }

    public async Task CopyTextAsync(string text)
    {
        await SetClipboardTextAsync(text);
        _logger.Info("Clipboard", $"已复制到剪贴板，文本长度={text.Length}");
    }

    private async Task SetClipboardTextAsync(string text)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    Clipboard.SetText(text);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.SetText(text));
                }

                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.Warn("Clipboard", $"剪贴板写入失败，第 {attempt + 1} 次: {ex.Message}");
                if (attempt < ClipboardRetryCount - 1)
                    await Task.Delay(ClipboardRetryDelayMs);
            }
        }

        if (lastError is not null)
            throw lastError;
    }

    private async Task TypeTextDirectlyAsync(string text)
    {
        _logger.Info("ClipboardFallback", $"开始直接输入，文本长度={text.Length}，前台窗口=\"{GetForegroundWindowTitle()}\"");

        foreach (var ch in text)
        {
            SendUnicodeChar(ch);
            await Task.Delay(1);
        }

        _logger.Info("ClipboardFallback", $"直接输入完成，当前前台窗口=\"{GetForegroundWindowTitle()}\"");
    }

    private static void SendUnicodeChar(char ch)
    {
        var inputs = new[]
        {
            new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        ScanCode = ch,
                        Flags = KEYEVENTF_UNICODE
                    }
                }
            },
            new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        ScanCode = ch,
                        Flags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                    }
                }
            }
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput 失败，已发送 {sent}/{inputs.Length}，Win32Error={Marshal.GetLastWin32Error()}");
    }

    private static string GetForegroundWindowTitle()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "";

            var builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return "";
        }
    }
}
