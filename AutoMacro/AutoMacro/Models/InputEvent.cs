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
    KeyCombo,
    ImageClick,
    WaitImage,
    WaitText,
    ClickText,
    ReadText,
    ReadData,
    SubmitData,
    Notify,
    WaitWindow,
    ActivateWindow,
    RecordedSegment,
    PasteText,
    ImageMove
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
    private int _recordedWindowWidth;

    [ObservableProperty]
    private int _recordedWindowHeight;

    [ObservableProperty]
    private bool _useWindowClientCoordinates;

    [ObservableProperty]
    private int _clientRelativeX;

    [ObservableProperty]
    private int _clientRelativeY;

    [ObservableProperty]
    private int _recordedClientWidth;

    [ObservableProperty]
    private int _recordedClientHeight;

    [ObservableProperty]
    private string? _windowTitle;

    [ObservableProperty]
    private string? _windowProcessName;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private double _matchThreshold = 0.92;

    [ObservableProperty]
    private int _timeoutMs = 10000;

    [ObservableProperty]
    private int _afterFoundDelayMs;

    [ObservableProperty]
    private string? _textPattern;

    [ObservableProperty]
    private bool _useOcrRegion;

    [ObservableProperty]
    private int _ocrRegionX;

    [ObservableProperty]
    private int _ocrRegionY;

    [ObservableProperty]
    private int _ocrRegionWidth;

    [ObservableProperty]
    private int _ocrRegionHeight;

    [ObservableProperty]
    private string? _requestUrl;

    [ObservableProperty]
    private string? _requestMethod;

    [ObservableProperty]
    private string? _requestBody;

    [ObservableProperty]
    private string? _responseVariableName;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private List<InputEvent>? _recordedEvents;

    [JsonIgnore]
    public int RecordedEventCount => RecordedEvents?.Count ?? 0;

    [JsonIgnore]
    public string ActionName => EventType switch
    {
        InputEventType.KeyDown => "按下按键",
        InputEventType.KeyUp => "松开按键",
        InputEventType.MouseDown => "按下鼠标",
        InputEventType.MouseUp => "松开鼠标",
        InputEventType.MouseMove => "移动鼠标",
        InputEventType.MouseWheel => "滚动鼠标",
        InputEventType.Delay => "等待",
        InputEventType.ClipboardPaste => "填写变量",
        InputEventType.KeyCombo => $"按键 {TextPattern}",
        InputEventType.ImageClick => "看到图片就点击",
        InputEventType.ImageMove => "看到图片就移动",
        InputEventType.WaitImage => "等待图片出现",
        InputEventType.WaitText => "等待文字出现",
        InputEventType.ClickText => "看到文字就点击",
        InputEventType.ReadText => "读取屏幕文字",
        InputEventType.ReadData => "读取数据",
        InputEventType.SubmitData => "提交结果",
        InputEventType.Notify => "完成后通知",
        InputEventType.WaitWindow => "等待窗口",
        InputEventType.ActivateWindow => "切换到窗口",
        InputEventType.RecordedSegment => "鼠标键盘录制",
        InputEventType.PasteText => $"插入文字: {PreviewText(TextPattern)}",
        _ => EventType.ToString()
    };

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
            ? $"移动鼠标: 窗口[{WindowTitle}] 内 ({RelativeX}, {RelativeY})"
            : $"移动鼠标: ({X}, {Y})",
        InputEventType.MouseWheel => $"滚轮: {WheelDelta}",
        InputEventType.Delay => $"等待: {DeltaMs}ms",
        InputEventType.ClipboardPaste => $"填写变量: {VariableMarker}",
        InputEventType.KeyCombo => $"按键操作: {TextPattern}",
        InputEventType.ImageClick => "找到图片后点击",
        InputEventType.ImageMove => "找到图片后移动鼠标",
        InputEventType.WaitImage => "等待图片出现",
        InputEventType.WaitText => $"等待文字出现: {TextPattern}{OcrRegionText}",
        InputEventType.ClickText => $"看到文字就点击: {TextPattern}{OcrRegionText}",
        InputEventType.ReadText => $"读取屏幕文字，并复制到剪贴板{OcrRegionText}",
        InputEventType.ReadData => $"读取数据: {RequestUrl}，保存为 {DisplayResponseVariableName}",
        InputEventType.SubmitData => $"提交结果: {RequestUrl}，保存返回为 {DisplayResponseVariableName}",
        InputEventType.Notify => $"完成后通知: {RequestUrl}",
        InputEventType.WaitWindow => $"等待窗口: {WindowTitle}",
        InputEventType.ActivateWindow => $"切换到窗口: {WindowTitle}",
        InputEventType.RecordedSegment => $"录制片段: {RecordedEventCount} 个动作",
        InputEventType.PasteText => $"粘贴文字: {PreviewText(TextPattern)}",
        _ => EventType.ToString()
    };

    private string OcrRegionText => UseOcrRegion
        ? $"，只识别框选区域({OcrRegionWidth}x{OcrRegionHeight})"
        : string.Empty;

    private string DisplayResponseVariableName =>
        string.IsNullOrWhiteSpace(ResponseVariableName) ? "返回数据" : ResponseVariableName;

    [JsonIgnore]
    public bool HasImage => EventType is InputEventType.ImageClick or InputEventType.ImageMove or InputEventType.WaitImage
                            && !string.IsNullOrWhiteSpace(ImagePath);

    [JsonIgnore]
    public bool CanEditTimeout => EventType is InputEventType.ImageClick or InputEventType.ImageMove or InputEventType.WaitImage
        or InputEventType.WaitText or InputEventType.ClickText
        or InputEventType.ReadData or InputEventType.SubmitData or InputEventType.Notify
        or InputEventType.WaitWindow or InputEventType.KeyCombo or InputEventType.RecordedSegment;

    [JsonIgnore]
    public bool CanEditAfterFoundDelay => EventType is InputEventType.ImageClick or InputEventType.ImageMove or InputEventType.KeyCombo
        or InputEventType.RecordedSegment or InputEventType.PasteText;

    [JsonIgnore]
    public string TimeoutText => EventType switch
    {
        InputEventType.ImageClick or InputEventType.ImageMove or InputEventType.WaitImage or InputEventType.WaitText or InputEventType.ClickText
            or InputEventType.ReadData or InputEventType.SubmitData or InputEventType.Notify or InputEventType.WaitWindow
            or InputEventType.KeyCombo or InputEventType.RecordedSegment
            => $"{TimeoutMs / 1000.0:0.#} 秒",
        _ => string.Empty
    };

    [JsonIgnore]
    public string AfterFoundDelayDisplayText => EventType switch
    {
        InputEventType.ImageClick or InputEventType.ImageMove => $"{AfterFoundDelayMs / 1000.0:0.#} 秒",
        InputEventType.KeyCombo or InputEventType.RecordedSegment or InputEventType.PasteText => $"{DeltaMs / 1000.0:0.#} 秒",
        _ => string.Empty
    };

    private static string PreviewText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "空";

        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 24 ? singleLine : $"{singleLine[..24]}...";
    }

    partial void OnEventTypeChanged(InputEventType value)
    {
        OnPropertyChanged(nameof(ActionName));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(CanEditTimeout));
        OnPropertyChanged(nameof(CanEditAfterFoundDelay));
        OnPropertyChanged(nameof(TimeoutText));
        OnPropertyChanged(nameof(AfterFoundDelayDisplayText));
    }

    partial void OnImagePathChanged(string? value) => OnPropertyChanged(nameof(HasImage));

    partial void OnTextPatternChanged(string? value)
    {
        OnPropertyChanged(nameof(ActionName));
        OnPropertyChanged(nameof(Details));
    }

    partial void OnDeltaMsChanged(long value)
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(AfterFoundDelayDisplayText));
    }

    partial void OnTimeoutMsChanged(int value)
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(TimeoutText));
    }

    partial void OnAfterFoundDelayMsChanged(int value)
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(AfterFoundDelayDisplayText));
    }

    partial void OnRecordedEventsChanged(List<InputEvent>? value)
    {
        OnPropertyChanged(nameof(RecordedEventCount));
        OnPropertyChanged(nameof(Details));
    }
}
