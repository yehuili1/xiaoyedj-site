using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SharpHook.Data;

namespace AutoMacro.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputEventType
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    MouseMove,
    MouseWheel,
    Delay,
    ClipboardPaste,
    ImageClick,
    WaitImage,
    WaitWindow,
    ActivateWindow
}

public partial class InputEvent : ObservableObject
{
    [ObservableProperty]
    private long _deltaMs;

    [ObservableProperty]
    private InputEventType _eventType;

    [ObservableProperty]
    private int _keyCode;

    [ObservableProperty]
    private int _mouseButton;

    [ObservableProperty]
    private int _x;

    [ObservableProperty]
    private int _y;

    [ObservableProperty]
    private int _wheelDelta;

    [ObservableProperty]
    private string? _variableMarker;

    [ObservableProperty]
    private bool _useWindowRelativeCoordinates;

    [ObservableProperty]
    private int _relativeX;

    [ObservableProperty]
    private int _relativeY;

    [ObservableProperty]
    private string? _windowTitle;

    [ObservableProperty]
    private string? _windowProcessName;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private double _matchThreshold = 0.92;

    [ObservableProperty]
    private int _timeoutMs = 5000;

    [JsonIgnore]
    public string Details => EventType switch
    {
        InputEventType.KeyDown => $"按下: {(KeyCode)KeyCode}",
        InputEventType.KeyUp => $"释放: {(KeyCode)KeyCode}",
        InputEventType.MouseDown => UseWindowRelativeCoordinates
            ? $"鼠标按下: 按键{MouseButton} 窗口[{WindowTitle}] 相对({RelativeX}, {RelativeY})"
            : $"鼠标按下: 按键{MouseButton} ({X}, {Y})",
        InputEventType.MouseUp => UseWindowRelativeCoordinates
            ? $"鼠标释放: 按键{MouseButton} 窗口[{WindowTitle}] 相对({RelativeX}, {RelativeY})"
            : $"鼠标释放: 按键{MouseButton} ({X}, {Y})",
        InputEventType.MouseMove => UseWindowRelativeCoordinates
            ? $"移动: 窗口[{WindowTitle}] 相对({RelativeX}, {RelativeY})"
            : $"移动: ({X}, {Y})",
        InputEventType.MouseWheel => $"滚轮: {WheelDelta}",
        InputEventType.Delay => $"延迟: {DeltaMs}ms",
        InputEventType.ClipboardPaste => $"粘贴变量: {VariableMarker}",
        InputEventType.ImageClick => $"图片点击: {Path.GetFileName(ImagePath)} 阈值{MatchThreshold:0.00} 超时{TimeoutMs}ms",
        InputEventType.WaitImage => $"等待图片: {Path.GetFileName(ImagePath)} 阈值{MatchThreshold:0.00} 超时{TimeoutMs}ms",
        InputEventType.WaitWindow => $"等待窗口: {WindowTitle} 超时{TimeoutMs}ms",
        InputEventType.ActivateWindow => $"激活窗口: {WindowTitle}",
        _ => EventType.ToString()
    };
}
