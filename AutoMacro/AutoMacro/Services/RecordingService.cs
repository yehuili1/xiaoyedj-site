using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoMacro.Models;
using SharpHook;
using SharpHook.Data;

namespace AutoMacro.Services;

public class RecordingService : IRecordingService
{
    private readonly IGlobalHookProvider _hookProvider;
    private readonly IHotkeyService _hotkeyService;
    private readonly IWindowAutomationService _windowAutomationService;
    private readonly Stopwatch _stopwatch = new();
    private readonly List<InputEvent> _events = new();
    private readonly HashSet<int> _pressedMouseButtons = new();
    private long _lastTimestamp;
    private short _lastMouseX, _lastMouseY;
    private bool _hasMousePosition;

    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;

    private static readonly (int VirtualKey, MouseButton Button)[] MouseButtonsToCapture =
    [
        (VkLeftButton, MouseButton.Button1),
        (VkRightButton, MouseButton.Button2),
        (VkMiddleButton, MouseButton.Button3),
        (VkXButton1, MouseButton.Button4),
        (VkXButton2, MouseButton.Button5)
    ];

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public event EventHandler<InputEvent>? EventRecorded;

    public RecordingService(
        IGlobalHookProvider hookProvider,
        IHotkeyService hotkeyService,
        IWindowAutomationService windowAutomationService)
    {
        _hookProvider = hookProvider;
        _hotkeyService = hotkeyService;
        _windowAutomationService = windowAutomationService;
    }

    public void StartRecording()
    {
        if (IsRecording) return;

        _events.Clear();
        _pressedMouseButtons.Clear();
        _lastTimestamp = 0;
        _hasMousePosition = false;
        _stopwatch.Restart();
        IsRecording = true;
        IsPaused = false;

        var hook = _hookProvider.Hook;
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        hook.MousePressed += OnMousePressed;
        hook.MouseReleased += OnMouseReleased;
        hook.MouseMoved += OnMouseMoved;
        hook.MouseDragged += OnMouseDragged;
        hook.MouseWheel += OnMouseWheel;

        RecordPressedMouseButtonsAtStart();
    }

    public void PauseRecording()
    {
        if (!IsRecording || IsPaused) return;
        IsPaused = true;
        _stopwatch.Stop();
    }

    public void ResumeRecording()
    {
        if (!IsRecording || !IsPaused) return;
        IsPaused = false;
        _stopwatch.Start();
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        IsPaused = false;
        _stopwatch.Stop();

        var hook = _hookProvider.Hook;
        hook.KeyPressed -= OnKeyPressed;
        hook.KeyReleased -= OnKeyReleased;
        hook.MousePressed -= OnMousePressed;
        hook.MouseReleased -= OnMouseReleased;
        hook.MouseMoved -= OnMouseMoved;
        hook.MouseDragged -= OnMouseDragged;
        hook.MouseWheel -= OnMouseWheel;
        _pressedMouseButtons.Clear();
    }

    public List<InputEvent> GetRecordedEvents() => new(_events);

    private bool ShouldSuppress(KeyCode keyCode, EventMask mask)
    {
        if (_hotkeyService is HotkeyService hs)
            return hs.ShouldSuppress(keyCode, mask);
        return false;
    }

    private long GetDelta()
    {
        var now = _stopwatch.ElapsedMilliseconds;
        var delta = now - _lastTimestamp;
        _lastTimestamp = now;
        return delta;
    }

    private void Record(InputEvent evt)
    {
        _events.Add(evt);
        EventRecorded?.Invoke(this, evt);
    }

    private void RecordPressedMouseButtonsAtStart()
    {
        if (!GetCursorPos(out var point))
            return;

        foreach (var (virtualKey, button) in MouseButtonsToCapture)
        {
            if ((GetAsyncKeyState(virtualKey) & 0x8000) == 0)
                continue;

            var buttonValue = (int)button;
            if (!_pressedMouseButtons.Add(buttonValue))
                continue;

            var x = ToShortCoordinate(point.X);
            var y = ToShortCoordinate(point.Y);
            RememberMousePosition(x, y);
            Record(AddWindowContext(new InputEvent
            {
                EventType = InputEventType.MouseDown,
                MouseButton = buttonValue,
                X = x,
                Y = y,
                DeltaMs = GetDelta()
            }));
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (IsPaused) return;
        if (ShouldSuppress(e.Data.KeyCode, e.RawEvent.Mask)) return;
        Record(new InputEvent
        {
            EventType = InputEventType.KeyDown,
            KeyCode = (int)e.Data.KeyCode,
            DeltaMs = GetDelta()
        });
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (IsPaused) return;
        if (ShouldSuppress(e.Data.KeyCode, e.RawEvent.Mask)) return;
        Record(new InputEvent
        {
            EventType = InputEventType.KeyUp,
            KeyCode = (int)e.Data.KeyCode,
            DeltaMs = GetDelta()
        });
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (IsPaused) return;
        _pressedMouseButtons.Add((int)e.Data.Button);
        RememberMousePosition(e.Data.X, e.Data.Y);
        Record(AddWindowContext(new InputEvent
        {
            EventType = InputEventType.MouseDown,
            MouseButton = (int)e.Data.Button,
            X = e.Data.X,
            Y = e.Data.Y,
            DeltaMs = GetDelta()
        }));
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (IsPaused) return;
        Record(AddWindowContext(new InputEvent
        {
            EventType = InputEventType.MouseUp,
            MouseButton = (int)e.Data.Button,
            X = e.Data.X,
            Y = e.Data.Y,
            DeltaMs = GetDelta()
        }));
        _pressedMouseButtons.Remove((int)e.Data.Button);
        RememberMousePosition(e.Data.X, e.Data.Y);
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        RecordMouseMove(e, false);
    }

    private void OnMouseDragged(object? sender, MouseHookEventArgs e)
    {
        RecordMouseMove(e, true);
    }

    private void RecordMouseMove(MouseHookEventArgs e, bool isDragEvent)
    {
        if (IsPaused) return;

        var threshold = isDragEvent || _pressedMouseButtons.Count > 0 ? 1 : 5;
        if (_hasMousePosition)
        {
            var dx = Math.Abs(e.Data.X - _lastMouseX);
            var dy = Math.Abs(e.Data.Y - _lastMouseY);
            if (dx < threshold && dy < threshold) return;
        }

        RememberMousePosition(e.Data.X, e.Data.Y);

        Record(AddWindowContext(new InputEvent
        {
            EventType = InputEventType.MouseMove,
            X = e.Data.X,
            Y = e.Data.Y,
            DeltaMs = GetDelta()
        }));
    }

    private void RememberMousePosition(short x, short y)
    {
        _lastMouseX = x;
        _lastMouseY = y;
        _hasMousePosition = true;
    }

    private static short ToShortCoordinate(int value)
    {
        return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        if (IsPaused) return;
        Record(AddWindowContext(new InputEvent
        {
            EventType = InputEventType.MouseWheel,
            WheelDelta = e.Data.Rotation,
            X = e.Data.X,
            Y = e.Data.Y,
            DeltaMs = GetDelta()
        }));
    }

    private InputEvent AddWindowContext(InputEvent evt)
    {
        var window = _windowAutomationService.GetForegroundWindowSnapshot();
        if (window is null)
            return evt;

        evt.WindowTitle = window.Title;
        evt.WindowProcessName = window.ProcessName;
        evt.RelativeX = evt.X - window.Left;
        evt.RelativeY = evt.Y - window.Top;
        evt.RecordedWindowWidth = window.Width;
        evt.RecordedWindowHeight = window.Height;
        evt.ClientRelativeX = evt.X - window.ClientLeft;
        evt.ClientRelativeY = evt.Y - window.ClientTop;
        evt.RecordedClientWidth = window.ClientWidth;
        evt.RecordedClientHeight = window.ClientHeight;
        evt.UseWindowRelativeCoordinates =
            evt.RelativeX >= 0 && evt.RelativeX <= window.Width &&
            evt.RelativeY >= 0 && evt.RelativeY <= window.Height;
        evt.UseWindowClientCoordinates =
            evt.ClientRelativeX >= 0 && evt.ClientRelativeX <= window.ClientWidth &&
            evt.ClientRelativeY >= 0 && evt.ClientRelativeY <= window.ClientHeight;

        return evt;
    }
}
