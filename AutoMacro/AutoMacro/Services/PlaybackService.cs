using System.IO;
using AutoMacro.Models;
using SharpHook;
using SharpHook.Data;

namespace AutoMacro.Services;

public class PlaybackService : IPlaybackService
{
    private readonly IClipboardInjector _clipboardInjector;
    private readonly IRunLogger _logger;
    private readonly IWindowAutomationService _windowAutomationService;
    private readonly IImageRecognitionService _imageRecognitionService;
    private readonly IOcrService _ocrService;
    private readonly IApiClientService _apiClientService;
    private readonly EventSimulator _simulator = new();
    private readonly Dictionary<string, string> _runtimeVariables = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _pauseTcs;

    private int _variableCounter;
    private double _playbackSpeed = 1.0;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public int CurrentLoop { get; private set; }
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler? LoopChanged;
    public event EventHandler<PlaybackStepChangedEventArgs>? StepChanged;

    public PlaybackService(
        IClipboardInjector clipboardInjector,
        IRunLogger logger,
        IWindowAutomationService windowAutomationService,
        IImageRecognitionService imageRecognitionService,
        IOcrService ocrService,
        IApiClientService apiClientService)
    {
        _clipboardInjector = clipboardInjector;
        _logger = logger;
        _windowAutomationService = windowAutomationService;
        _imageRecognitionService = imageRecognitionService;
        _ocrService = ocrService;
        _apiClientService = apiClientService;
    }

    public async Task StartPlaybackAsync(IList<InputEvent> events, VariableTable variableTable, int loopCount, double playbackSpeed)
    {
        if (IsPlaying) return;

        _cts = new CancellationTokenSource();
        _pauseTcs = null;
        IsPlaying = true;
        IsPaused = false;
        CurrentLoop = 0;
        _variableCounter = 0;
        _runtimeVariables.Clear();
        _playbackSpeed = NormalizePlaybackSpeed(playbackSpeed);
        _logger.Info("Playback", $"开始回放: actions={events.Count}, rows={variableTable.RowCount}, loopCount={loopCount}, speed={_playbackSpeed:0.##}x");
        PlaybackStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var totalLoops = loopCount == 0 ? int.MaxValue : loopCount;

            var filtered = PreparePlaybackEvents(events);

            for (int loop = 0; loop < totalLoops; loop++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                CurrentLoop = loop + 1;
                LoopChanged?.Invoke(this, EventArgs.Empty);

                for (var stepIndex = 0; stepIndex < filtered.Count; stepIndex++)
                {
                    var evt = filtered[stepIndex];
                    _cts.Token.ThrowIfCancellationRequested();
                    if (IsPaused && _pauseTcs is not null)
                        await _pauseTcs.Task; // 异步等待，不阻塞 UI

                    StepChanged?.Invoke(this, new PlaybackStepChangedEventArgs(evt, stepIndex + 1, filtered.Count, CurrentLoop));

                    if (evt.EventType != InputEventType.Delay && evt.DeltaMs > 0)
                        await DelayAsync(evt.DeltaMs, _cts.Token);

                    await ExecuteEvent(evt, variableTable, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Playback", "回放取消/停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Playback", $"回放异常: loop={CurrentLoop}, variableIndex={_variableCounter}", ex, captureScreenshot: true);
        }
        finally
        {
            IsPlaying = false;
            IsPaused = false;
            _pauseTcs?.TrySetResult(true);
            _logger.Info("Playback", $"回放结束: loop={CurrentLoop}, variableIndex={_variableCounter}");
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PausePlayback()
    {
        if (!IsPlaying || IsPaused) return;
        IsPaused = true;
        _pauseTcs = new TaskCompletionSource<bool>();
        _logger.Info("Playback", "回放暂停");
    }

    public void ResumePlayback()
    {
        if (!IsPlaying || !IsPaused) return;
        IsPaused = false;
        _pauseTcs?.TrySetResult(true);
        _logger.Info("Playback", "回放继续");
    }

    public void StopPlayback()
    {
        _pauseTcs?.TrySetResult(true); // 如果暂停中，先恢复让循环退出
        _cts?.Cancel();
        _logger.Info("Playback", "请求停止回放");
    }

    public void SetPlaybackSpeed(double playbackSpeed)
    {
        _playbackSpeed = NormalizePlaybackSpeed(playbackSpeed);
        _logger.Info("Playback", $"回放倍速调整为 {_playbackSpeed:0.##}x");
    }

    private async Task ExecuteEvent(InputEvent evt, VariableTable variableTable, CancellationToken cancellationToken)
    {
        switch (evt.EventType)
        {
            case InputEventType.KeyDown:
                _simulator.SimulateKeyPress((KeyCode)evt.KeyCode);
                break;

            case InputEventType.KeyUp:
                _simulator.SimulateKeyRelease((KeyCode)evt.KeyCode);
                break;

            case InputEventType.KeyCombo:
                await SimulateKeyComboWithTimeoutAsync(evt, cancellationToken);
                break;

            case InputEventType.RecordedSegment:
                await ExecuteRecordedSegmentAsync(evt, variableTable, cancellationToken);
                break;

            case InputEventType.MouseDown:
                var (mouseDownX, mouseDownY) = ResolvePoint(evt);
                _simulator.SimulateMousePress((short)mouseDownX, (short)mouseDownY, (MouseButton)evt.MouseButton);
                break;

            case InputEventType.MouseUp:
                var (mouseUpX, mouseUpY) = ResolvePoint(evt);
                _simulator.SimulateMouseRelease((short)mouseUpX, (short)mouseUpY, (MouseButton)evt.MouseButton);
                break;

            case InputEventType.MouseMove:
                var (moveX, moveY) = ResolvePoint(evt);
                _simulator.SimulateMouseMovement((short)moveX, (short)moveY);
                break;

            case InputEventType.MouseWheel:
                _simulator.SimulateMouseWheel((short)evt.WheelDelta, MouseWheelScrollDirection.Vertical, MouseWheelScrollType.UnitScroll);
                break;

            case InputEventType.ClipboardPaste:
                if (evt.VariableMarker is not null)
                {
                    var value = variableTable.GetValue(_variableCounter);

                    if (value is null)
                    {
                        _logger.Warn("Playback", $"变量行已用完，停止回放: index={_variableCounter}, rows={variableTable.RowCount}");
                        _cts?.Cancel();
                        return;
                    }

                    _logger.Info("Playback", $"粘贴变量: row={_variableCounter + 1}, length={value.Length}");
                    await _clipboardInjector.InjectTextAsync(value, cancellationToken);

                    _variableCounter++;
                }
                break;

            case InputEventType.PasteText:
                {
                    var text = evt.TextPattern ?? string.Empty;
                    if (string.IsNullOrEmpty(text))
                    {
                        _logger.Warn("Playback", "插入文字内容为空，已跳过");
                        break;
                    }

                    await _clipboardInjector.InjectTextAsync(text, cancellationToken);
                    _logger.Info("Playback", $"插入文字完成: length={text.Length}");
                    break;
                }

            case InputEventType.Delay:
                await DelayAsync(evt.DeltaMs, cancellationToken);
                break;

            case InputEventType.ActivateWindow:
                if (!_windowAutomationService.ActivateWindow(evt.WindowTitle ?? string.Empty))
                    _logger.Error("Playback", $"激活窗口失败: {evt.WindowTitle}", captureScreenshot: true);
                break;

            case InputEventType.WaitWindow:
                if (!await _windowAutomationService.WaitForWindowAsync(evt.WindowTitle ?? string.Empty, evt.TimeoutMs, cancellationToken))
                    _cts?.Cancel();
                break;

            case InputEventType.WaitImage:
                {
                    var found = await _imageRecognitionService.FindImageAsync(
                        evt.ImagePath ?? string.Empty,
                        evt.MatchThreshold,
                        evt.TimeoutMs,
                        cancellationToken);
                    if (!found.Found)
                    {
                        SkipMissingImage(evt, "等待图片");
                    }
                    break;
                }

            case InputEventType.ImageClick:
                {
                    var found = await _imageRecognitionService.FindImageAsync(
                        evt.ImagePath ?? string.Empty,
                        evt.MatchThreshold,
                        evt.TimeoutMs,
                        cancellationToken);
                    if (!found.Found)
                    {
                        SkipMissingImage(evt, "图片点击");
                        break;
                    }

                    if (evt.AfterFoundDelayMs > 0)
                        await Task.Delay(evt.AfterFoundDelayMs, cancellationToken);

                    _simulator.SimulateMouseMovement((short)found.X, (short)found.Y);
                    await Task.Delay(20, cancellationToken);
                    _simulator.SimulateMousePress((short)found.X, (short)found.Y, MouseButton.Button1);
                    await Task.Delay(20, cancellationToken);
                    _simulator.SimulateMouseRelease((short)found.X, (short)found.Y, MouseButton.Button1);
                    _logger.Info("Playback", $"图片点击完成: {Path.GetFileName(evt.ImagePath)}, x={found.X}, y={found.Y}, score={found.Score:0.000}");
                    break;
                }

            case InputEventType.WaitText:
                {
                    var found = await _ocrService.FindTextAsync(
                        evt.TextPattern ?? string.Empty,
                        evt.TimeoutMs,
                        BuildOcrRegion(evt),
                        cancellationToken);
                    if (!found.Found)
                    {
                        _logger.Error("OCR", $"等待文字失败: {evt.TextPattern}, {found.ErrorMessage}", captureScreenshot: true);
                        _cts?.Cancel();
                    }
                    break;
                }

            case InputEventType.ClickText:
                {
                    var found = await _ocrService.FindTextAsync(
                        evt.TextPattern ?? string.Empty,
                        evt.TimeoutMs,
                        BuildOcrRegion(evt),
                        cancellationToken);
                    if (!found.Found)
                    {
                        _logger.Error("OCR", $"点击文字失败: {evt.TextPattern}, {found.ErrorMessage}", captureScreenshot: true);
                        _cts?.Cancel();
                        break;
                    }

                    var x = found.X + found.Width / 2;
                    var y = found.Y + found.Height / 2;
                    _simulator.SimulateMouseMovement((short)x, (short)y);
                    await Task.Delay(20, cancellationToken);
                    _simulator.SimulateMousePress((short)x, (short)y, MouseButton.Button1);
                    await Task.Delay(20, cancellationToken);
                    _simulator.SimulateMouseRelease((short)x, (short)y, MouseButton.Button1);
                    _logger.Info("OCR", $"文字点击完成: \"{found.Text}\", x={x}, y={y}");
                    break;
                }

            case InputEventType.ReadText:
                {
                    var read = await _ocrService.RecognizeScreenAsync(BuildOcrRegion(evt), cancellationToken);
                    if (!read.Success)
                    {
                        _logger.Error("OCR", $"读取屏幕文字失败: {read.ErrorMessage}", captureScreenshot: true);
                        _cts?.Cancel();
                        break;
                    }

                    await _clipboardInjector.CopyTextAsync(read.Text, cancellationToken);
                    SaveRuntimeVariable("识别文字", read.Text);
                    _logger.Info("OCR", $"读取屏幕文字完成，已复制到剪贴板，length={read.Text.Length}");
                    break;
                }

            case InputEventType.ReadData:
            case InputEventType.SubmitData:
            case InputEventType.Notify:
                {
                    var method = string.IsNullOrWhiteSpace(evt.RequestMethod)
                        ? evt.EventType == InputEventType.ReadData ? "GET" : "POST"
                        : evt.RequestMethod;
                    var url = ExpandRuntimeVariables(evt.RequestUrl ?? string.Empty);
                    var body = ExpandRuntimeVariables(evt.RequestBody ?? string.Empty);
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(Math.Max(1000, evt.TimeoutMs <= 0 ? 10000 : evt.TimeoutMs));
                    var result = await _apiClientService.SendAsync(method, url, body, timeoutCts.Token);
                    if (!result.Success)
                    {
                        _logger.Error("API", $"请求失败: {url}, status={result.StatusCode}, {result.ErrorMessage}", captureScreenshot: true);
                        _cts?.Cancel();
                        break;
                    }

                    var variableName = string.IsNullOrWhiteSpace(evt.ResponseVariableName)
                        ? "返回数据"
                        : evt.ResponseVariableName.Trim();
                    SaveRuntimeVariable(variableName, result.Body);

                    if (evt.EventType == InputEventType.ReadData)
                        await _clipboardInjector.CopyTextAsync(result.Body, cancellationToken);

                    _logger.Info("API", $"{evt.ActionName}完成，保存变量 \"{variableName}\"，length={result.Body.Length}");
                    break;
                }
        }
    }

    private (int X, int Y) ResolvePoint(InputEvent evt)
    {
        return _windowAutomationService.TryResolvePoint(evt, out var x, out var y)
            ? (x, y)
            : (evt.X, evt.Y);
    }

    private async Task SimulateKeyComboAsync(string? comboText, CancellationToken cancellationToken)
    {
        var keys = BuildKeyComboKeys(comboText);
        if (keys.Count == 0)
        {
            _logger.Error("Playback", $"按键操作无效: {comboText}");
            _cts?.Cancel();
            return;
        }

        foreach (var key in keys)
        {
            _simulator.SimulateKeyPress(key);
            await Task.Delay(20, cancellationToken);
        }

        for (var i = keys.Count - 1; i >= 0; i--)
        {
            _simulator.SimulateKeyRelease(keys[i]);
            await Task.Delay(20, cancellationToken);
        }

        _logger.Info("Playback", $"按键操作完成: {comboText}");
    }

    private async Task SimulateKeyComboWithTimeoutAsync(InputEvent evt, CancellationToken cancellationToken)
    {
        var timeoutMs = Math.Max(1000, evt.TimeoutMs <= 0 ? 10000 : evt.TimeoutMs);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await SimulateKeyComboAsync(evt.TextPattern, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Error("Playback", $"按键操作超时: {evt.TextPattern}, timeout={timeoutMs}ms");
            _cts?.Cancel();
        }
    }

    private async Task ExecuteRecordedSegmentAsync(InputEvent segment, VariableTable variableTable, CancellationToken cancellationToken)
    {
        if (segment.RecordedEvents is null || segment.RecordedEvents.Count == 0)
        {
            _logger.Warn("Playback", "鼠标键盘录制片段为空，已跳过");
            return;
        }

        var timeoutMs = Math.Max(1000, segment.TimeoutMs <= 0 ? 10000 : segment.TimeoutMs);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var childEvents = PreparePlaybackEvents(segment.RecordedEvents, resetFirstDelay: true, collapseDragDuration: true);
            for (var i = 0; i < childEvents.Count; i++)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();

                if (IsPaused && _pauseTcs is not null)
                    await _pauseTcs.Task;

                var child = childEvents[i];
                if (child.EventType != InputEventType.Delay && child.DeltaMs > 0)
                    await DelayAsync(child.DeltaMs, timeoutCts.Token);

                await ExecuteEvent(child, variableTable, timeoutCts.Token);
            }

            _logger.Info("Playback", $"鼠标键盘录制片段完成: actions={childEvents.Count}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Error("Playback", $"鼠标键盘录制片段超时: timeout={timeoutMs}ms, actions={segment.RecordedEventCount}");
            _cts?.Cancel();
        }
    }

    private static List<KeyCode> BuildKeyComboKeys(string? comboText)
    {
        if (string.IsNullOrWhiteSpace(comboText))
            return new List<KeyCode>();

        var binding = HotkeySettings.ParseHotkey(comboText);
        if (!binding.IsValid)
            return new List<KeyCode>();

        var keys = new List<KeyCode>();
        if (binding.Ctrl) keys.Add(KeyCode.VcLeftControl);
        if (binding.Shift) keys.Add(KeyCode.VcLeftShift);
        if (binding.Alt) keys.Add(KeyCode.VcLeftAlt);
        keys.Add(binding.Key);
        return keys;
    }

    private static double NormalizePlaybackSpeed(double playbackSpeed)
    {
        if (double.IsNaN(playbackSpeed) || double.IsInfinity(playbackSpeed))
            return 1.0;

        return Math.Clamp(playbackSpeed, 0.1, 10.0);
    }

    private Task DelayAsync(long deltaMs, CancellationToken cancellationToken)
    {
        if (deltaMs <= 0) return Task.CompletedTask;

        var scaled = deltaMs / _playbackSpeed;
        var delayMs = (int)Math.Clamp(Math.Round(scaled), 0, int.MaxValue);
        return delayMs == 0
            ? Task.CompletedTask
            : Task.Delay(delayMs, cancellationToken);
    }

    private static List<InputEvent> PreparePlaybackEvents(
        IEnumerable<InputEvent> events,
        bool resetFirstDelay = false,
        bool collapseDragDuration = false)
    {
        var filtered = new List<InputEvent>();
        long pendingDelta = 0;
        var pressedButtons = new HashSet<int>();
        var orphanMoves = new List<InputEvent>();
        var dragMoves = new List<InputEvent>();

        foreach (var evt in events)
        {
            switch (evt.EventType)
            {
                case InputEventType.MouseDown:
                    pendingDelta += SumDeltas(orphanMoves);
                    orphanMoves.Clear();
                    FlushCollapsedDragMoves(filtered, dragMoves, null, ref pendingDelta, collapseDragDuration);
                    pressedButtons.Add(evt.MouseButton);
                    filtered.Add(CloneEvent(evt, evt.DeltaMs + pendingDelta));
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp when pressedButtons.Remove(evt.MouseButton):
                    FlushCollapsedDragMoves(filtered, dragMoves, evt, ref pendingDelta, collapseDragDuration);
                    filtered.Add(CloneEvent(evt, evt.DeltaMs + pendingDelta));
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp when orphanMoves.Count > 0:
                    AddRepairedDrag(filtered, orphanMoves, evt, pendingDelta, collapseDragDuration);
                    orphanMoves.Clear();
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp:
                    pendingDelta += evt.DeltaMs;
                    break;

                case InputEventType.MouseMove when pressedButtons.Count == 0:
                    orphanMoves.Add(evt);
                    break;

                case InputEventType.MouseMove:
                    dragMoves.Add(evt);
                    break;

                default:
                    pendingDelta += SumDeltas(orphanMoves);
                    orphanMoves.Clear();
                    FlushCollapsedDragMoves(filtered, dragMoves, null, ref pendingDelta, collapseDragDuration);
                    filtered.Add(CloneEvent(evt, evt.DeltaMs + pendingDelta));
                    pendingDelta = 0;
                    break;
            }
        }

        if (resetFirstDelay && filtered.Count > 0)
            filtered[0].DeltaMs = 0;

        return filtered;
    }

    private static void AddRepairedDrag(
        ICollection<InputEvent> target,
        IReadOnlyList<InputEvent> orphanMoves,
        InputEvent mouseUp,
        long pendingDelta,
        bool collapseDragDuration)
    {
        var firstMove = orphanMoves[0];
        var mouseDownDelta = collapseDragDuration ? 0 : firstMove.DeltaMs + pendingDelta;
        target.Add(CloneMouseDownFromMove(firstMove, mouseUp.MouseButton, mouseDownDelta));

        var finalMoveDelta = collapseDragDuration ? 0 : SumDeltas(orphanMoves.Skip(1));
        target.Add(CloneMouseMoveFromEvent(mouseUp, finalMoveDelta));
        target.Add(CloneEvent(mouseUp, collapseDragDuration ? 0 : mouseUp.DeltaMs));
    }

    private static void FlushCollapsedDragMoves(
        ICollection<InputEvent> target,
        List<InputEvent> dragMoves,
        InputEvent? finalPoint,
        ref long pendingDelta,
        bool collapseDragDuration)
    {
        if (dragMoves.Count == 0)
            return;

        var source = finalPoint ?? dragMoves[^1];
        var deltaMs = collapseDragDuration ? 0 : SumDeltas(dragMoves) + pendingDelta;
        target.Add(CloneMouseMoveFromEvent(source, deltaMs));
        dragMoves.Clear();
        pendingDelta = 0;
    }

    private static InputEvent CloneMouseDownFromMove(InputEvent source, int mouseButton, long deltaMs)
    {
        var clone = CloneEvent(source, deltaMs);
        clone.EventType = InputEventType.MouseDown;
        clone.MouseButton = mouseButton;
        return clone;
    }

    private static InputEvent CloneMouseMoveFromEvent(InputEvent source, long deltaMs)
    {
        var clone = CloneEvent(source, deltaMs);
        clone.EventType = InputEventType.MouseMove;
        clone.MouseButton = 0;
        return clone;
    }

    private static long SumDeltas(IEnumerable<InputEvent> events)
    {
        var total = 0L;
        foreach (var evt in events)
            total += evt.DeltaMs;

        return total;
    }

    private static InputEvent CloneEvent(InputEvent evt, long deltaMs)
    {
        return new InputEvent
        {
            DeltaMs = deltaMs,
            EventType = evt.EventType,
            KeyCode = evt.KeyCode,
            MouseButton = evt.MouseButton,
            X = evt.X,
            Y = evt.Y,
            WheelDelta = evt.WheelDelta,
            VariableMarker = evt.VariableMarker,
            UseWindowRelativeCoordinates = evt.UseWindowRelativeCoordinates,
            RelativeX = evt.RelativeX,
            RelativeY = evt.RelativeY,
            RecordedWindowWidth = evt.RecordedWindowWidth,
            RecordedWindowHeight = evt.RecordedWindowHeight,
            UseWindowClientCoordinates = evt.UseWindowClientCoordinates,
            ClientRelativeX = evt.ClientRelativeX,
            ClientRelativeY = evt.ClientRelativeY,
            RecordedClientWidth = evt.RecordedClientWidth,
            RecordedClientHeight = evt.RecordedClientHeight,
            WindowTitle = evt.WindowTitle,
            WindowProcessName = evt.WindowProcessName,
            ImagePath = evt.ImagePath,
            MatchThreshold = evt.MatchThreshold,
            TimeoutMs = evt.TimeoutMs,
            AfterFoundDelayMs = evt.AfterFoundDelayMs,
            TextPattern = evt.TextPattern,
            UseOcrRegion = evt.UseOcrRegion,
            OcrRegionX = evt.OcrRegionX,
            OcrRegionY = evt.OcrRegionY,
            OcrRegionWidth = evt.OcrRegionWidth,
            OcrRegionHeight = evt.OcrRegionHeight,
            RequestUrl = evt.RequestUrl,
            RequestMethod = evt.RequestMethod,
            RequestBody = evt.RequestBody,
            ResponseVariableName = evt.ResponseVariableName,
            RecordedEvents = CloneRecordedEvents(evt.RecordedEvents)
        };
    }

    private static List<InputEvent>? CloneRecordedEvents(IEnumerable<InputEvent>? events)
    {
        return events?.Select(child => CloneEvent(child, child.DeltaMs)).ToList();
    }

    private void SaveRuntimeVariable(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _runtimeVariables[name.Trim()] = value;
    }

    private string ExpandRuntimeVariables(string value)
    {
        var expanded = value;
        foreach (var variable in _runtimeVariables)
            expanded = expanded.Replace($"{{{{{variable.Key}}}}}", variable.Value, StringComparison.OrdinalIgnoreCase);

        return expanded;
    }

    private static OcrRegion? BuildOcrRegion(InputEvent evt)
    {
        return evt.UseOcrRegion && evt.OcrRegionWidth > 0 && evt.OcrRegionHeight > 0
            ? new OcrRegion(evt.OcrRegionX, evt.OcrRegionY, evt.OcrRegionWidth, evt.OcrRegionHeight)
            : null;
    }

    private void SkipMissingImage(InputEvent evt, string actionName)
    {
        var imageName = Path.GetFileName(evt.ImagePath);
        _logger.Warn("Playback", $"{actionName}未找到，已自动跳过: {imageName}, timeout={evt.TimeoutMs}ms");
    }
}
