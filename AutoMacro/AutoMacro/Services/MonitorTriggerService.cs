using System.Diagnostics;
using System.IO;
using AutoMacro.Models;

namespace AutoMacro.Services;

public class MonitorTriggerService : IMonitorTriggerService
{
    private readonly IImageRecognitionService _imageRecognitionService;
    private readonly IOcrService _ocrService;
    private readonly IWindowAutomationService _windowAutomationService;
    private readonly IRunLogger _logger;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _cts;
    private Task? _monitorLoop;
    private volatile bool _suspended;
    private RecordProfile? _activeProfile;
    private MonitorTriggerSettings? _activeSettings;

    public bool IsRunning { get; private set; }
    public RecordProfile? ActiveProfile => _activeProfile;

    public event EventHandler<MonitorStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<MonitorTriggeredEventArgs>? Triggered;

    public MonitorTriggerService(
        IImageRecognitionService imageRecognitionService,
        IOcrService ocrService,
        IWindowAutomationService windowAutomationService,
        IRunLogger logger)
    {
        _imageRecognitionService = imageRecognitionService;
        _ocrService = ocrService;
        _windowAutomationService = windowAutomationService;
        _logger = logger;
    }

    public async Task StartAsync(RecordProfile profile)
    {
        await StopAsync();

        var settings = profile.MonitorSettings.Clone();
        settings.Normalize();
        if (!settings.IsConfigured)
            throw new InvalidOperationException("请先设置要监控的图片、文字或窗口。");

        lock (_stateLock)
        {
            _activeProfile = profile;
            _activeSettings = settings;
            _suspended = false;
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _monitorLoop = Task.Run(() => RunMonitorLoopAsync(profile, settings, _cts.Token));
        }

        _logger.Info(
            "Monitor",
            $"开始监控: profile={profile.Name}, type={settings.TriggerType}, interval={settings.ScanIntervalMs}ms");
        RaiseStatus($"正在监控：{settings.TriggerTypeText} · {settings.RegionText}");
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_stateLock)
        {
            cts = _cts;
            loop = _monitorLoop;
            _cts = null;
            _monitorLoop = null;
            _suspended = false;
        }

        if (cts is not null)
        {
            cts.Cancel();
            if (loop is not null)
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts.Dispose();
        }

        if (IsRunning)
            _logger.Info("Monitor", "监控已停止");

        IsRunning = false;
        _activeProfile = null;
        _activeSettings = null;
        RaiseStatus("监控已停止");
    }

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (IsRunning)
            RaiseStatus(suspended ? "脚本执行中，监控已暂停" : "脚本执行完成，等待目标重新出现");
    }

    public async Task<MonitorProbeResult> TestOnceAsync(
        MonitorTriggerSettings settings,
        CancellationToken cancellationToken,
        bool maskOwnWindows = true)
    {
        var normalized = settings.Clone();
        normalized.Normalize();
        if (!normalized.IsConfigured)
            return new MonitorProbeResult(false, "监控条件还没有配置完整。");

        switch (normalized.TriggerType)
        {
            case MonitorTriggerType.Image:
                if (!File.Exists(normalized.ImagePath))
                    return new MonitorProbeResult(false, "目标图片不存在，请重新选择。", 0);

                var image = await _imageRecognitionService.FindImageOnceAsync(
                    normalized.ImagePath,
                    normalized.MatchThreshold,
                    normalized.GetRegion(),
                    cancellationToken,
                    maskOwnWindows);
                return image.Found
                    ? new MonitorProbeResult(true, $"已找到目标图片，匹配度 {image.Score:P0}。", image.Score)
                    : new MonitorProbeResult(false, $"本次没有找到目标图片，最高匹配度 {image.Score:P0}。", image.Score);

            case MonitorTriggerType.Text:
                var text = await _ocrService.FindTextOnceAsync(
                    normalized.TextPattern,
                    normalized.GetRegion(),
                    cancellationToken,
                    maskOwnWindows);
                return text.Found
                    ? new MonitorProbeResult(true, $"已找到文字“{normalized.TextPattern}”。", 1)
                    : new MonitorProbeResult(false, $"本次没有找到文字“{normalized.TextPattern}”。", 0);

            case MonitorTriggerType.Window:
                var foundWindow = _windowAutomationService.IsWindowAvailable(normalized.WindowTitle);
                return foundWindow
                    ? new MonitorProbeResult(true, $"已找到窗口“{normalized.WindowTitle}”。", 1)
                    : new MonitorProbeResult(false, $"本次没有找到窗口“{normalized.WindowTitle}”。", 0);

            default:
                return new MonitorProbeResult(false, "不支持的监控类型。");
        }
    }

    private async Task RunMonitorLoopAsync(
        RecordProfile profile,
        MonitorTriggerSettings settings,
        CancellationToken cancellationToken)
    {
        var armed = true;
        var consecutiveMatches = 0;
        var cooldownUntil = DateTime.MinValue;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_suspended)
                {
                    await Task.Delay(200, cancellationToken);
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                var result = await TestOnceAsync(settings, cancellationToken);
                var now = DateTime.Now;

                if (result.Matched)
                {
                    if (armed && now >= cooldownUntil)
                    {
                        consecutiveMatches++;
                        RaiseStatus(
                            consecutiveMatches >= settings.ConsecutiveMatches
                                ? "识别成功，正在启动脚本"
                                : $"已识别 {consecutiveMatches}/{settings.ConsecutiveMatches} 次，正在确认",
                            result.Score);

                        if (consecutiveMatches >= settings.ConsecutiveMatches)
                        {
                            armed = false;
                            consecutiveMatches = 0;
                            cooldownUntil = now.AddSeconds(settings.CooldownSeconds);
                            _logger.Info(
                                "Monitor",
                                $"触发成功: profile={profile.Name}, type={settings.TriggerType}, score={result.Score:0.000}");
                            Triggered?.Invoke(this, new MonitorTriggeredEventArgs(profile, result));
                        }
                    }
                    else
                    {
                        RaiseStatus("目标仍然存在，等待目标消失后重新布防", result.Score);
                    }
                }
                else
                {
                    consecutiveMatches = 0;
                    if (!armed && now >= cooldownUntil)
                    {
                        armed = true;
                        RaiseStatus("目标已消失，监控已重新布防");
                    }
                    else if (armed)
                    {
                        RaiseStatus("正在监控，尚未发现目标", result.Score);
                    }
                    else
                    {
                        var remaining = Math.Max(0, (int)Math.Ceiling((cooldownUntil - now).TotalSeconds));
                        RaiseStatus($"冷却中，还剩 {remaining} 秒");
                    }
                }

                stopwatch.Stop();
                var minimumInterval = settings.TriggerType == MonitorTriggerType.Text ? 700 : 300;
                var interval = Math.Max(minimumInterval, settings.ScanIntervalMs);
                var wait = Math.Max(50, interval - (int)stopwatch.ElapsedMilliseconds);
                await Task.Delay(wait, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Monitor", "监控循环异常停止", ex, captureScreenshot: false);
            RaiseStatus($"监控异常：{ex.Message}");
            IsRunning = false;
        }
    }

    private void RaiseStatus(string status, double? score = null)
    {
        StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs(status, score));
    }
}
