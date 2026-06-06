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
    private readonly EventSimulator _simulator = new();
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

    public PlaybackService(
        IClipboardInjector clipboardInjector,
        IRunLogger logger,
        IWindowAutomationService windowAutomationService,
        IImageRecognitionService imageRecognitionService)
    {
        _clipboardInjector = clipboardInjector;
        _logger = logger;
        _windowAutomationService = windowAutomationService;
        _imageRecognitionService = imageRecognitionService;
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
        _playbackSpeed = NormalizePlaybackSpeed(playbackSpeed);
        _logger.Info("Playback", $"开始回放: actions={events.Count}, rows={variableTable.RowCount}, loopCount={loopCount}, speed={_playbackSpeed:0.##}x");
        PlaybackStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var totalLoops = loopCount == 0 ? int.MaxValue : loopCount;

            // 预处理：去掉空闲 MouseMove（无按键按下时的移动），保留拖拽中的 MouseMove
            var filtered = new List<InputEvent>();
            long pendingDelta = 0;
            bool mouseDown = false;
            foreach (var evt in events)
            {
                if (evt.EventType == InputEventType.MouseDown)
                    mouseDown = true;
                else if (evt.EventType == InputEventType.MouseUp)
                    mouseDown = false;

                if (evt.EventType == InputEventType.MouseMove && !mouseDown)
                {
                    // 空闲移动：跳过，累加时间差
                    pendingDelta += evt.DeltaMs;
                }
                else
                {
                    filtered.Add(CloneEvent(evt, evt.DeltaMs + pendingDelta));
                    pendingDelta = 0;
                }
            }

            for (int loop = 0; loop < totalLoops; loop++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                CurrentLoop = loop + 1;
                LoopChanged?.Invoke(this, EventArgs.Empty);

                foreach (var evt in filtered)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    if (IsPaused && _pauseTcs is not null)
                        await _pauseTcs.Task; // 异步等待，不阻塞 UI

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
                    await _clipboardInjector.InjectTextAsync(value);

                    _variableCounter++;
                }
                break;

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
                        _cts?.Cancel();
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
                        _cts?.Cancel();
                        break;
                    }

                    _simulator.SimulateMouseMovement((short)found.X, (short)found.Y);
                    await Task.Delay(50, cancellationToken);
                    _simulator.SimulateMousePress((short)found.X, (short)found.Y, MouseButton.Button1);
                    await Task.Delay(50, cancellationToken);
                    _simulator.SimulateMouseRelease((short)found.X, (short)found.Y, MouseButton.Button1);
                    _logger.Info("Playback", $"图片点击完成: {Path.GetFileName(evt.ImagePath)}, x={found.X}, y={found.Y}, score={found.Score:0.000}");
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
            WindowTitle = evt.WindowTitle,
            WindowProcessName = evt.WindowProcessName,
            ImagePath = evt.ImagePath,
            MatchThreshold = evt.MatchThreshold,
            TimeoutMs = evt.TimeoutMs
        };
    }
}
