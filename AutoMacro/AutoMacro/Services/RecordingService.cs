using System.Diagnostics;
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
    private long _lastTimestamp;
    private short _lastMouseX, _lastMouseY;

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
        _lastTimestamp = 0;
        _stopwatch.Restart();
        IsRecording = true;
        IsPaused = false;

        var hook = _hookProvider.Hook;
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        hook.MousePressed += OnMousePressed;
        hook.MouseReleased += OnMouseReleased;
        hook.MouseMoved += OnMouseMoved;
        hook.MouseWheel += OnMouseWheel;
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
        hook.MouseWheel -= OnMouseWheel;
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
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        if (IsPaused) return;
        var dx = Math.Abs(e.Data.X - _lastMouseX);
        var dy = Math.Abs(e.Data.Y - _lastMouseY);
        if (dx < 5 && dy < 5) return;

        _lastMouseX = e.Data.X;
        _lastMouseY = e.Data.Y;

        Record(AddWindowContext(new InputEvent
        {
            EventType = InputEventType.MouseMove,
            X = e.Data.X,
            Y = e.Data.Y,
            DeltaMs = GetDelta()
        }));
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
        evt.UseWindowRelativeCoordinates =
            evt.RelativeX >= 0 && evt.RelativeX <= window.Width &&
            evt.RelativeY >= 0 && evt.RelativeY <= window.Height;

        return evt;
    }
}
