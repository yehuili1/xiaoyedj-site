using AutoMacro.Models;
using AutoMacro.Services;
using Xunit;

namespace AutoMacro.Tests;

public class MonitorTriggerServiceTests
{
    [Fact]
    public void Normalize_ClampsUnsafeValuesAndClearsInvalidRegion()
    {
        var settings = new MonitorTriggerSettings
        {
            MatchThreshold = 2,
            ScanIntervalMs = 1,
            ConsecutiveMatches = 99,
            CooldownSeconds = -5,
            UseRegion = true,
            RegionWidth = 0,
            RegionHeight = 100
        };

        settings.Normalize();

        Assert.Equal(1, settings.MatchThreshold);
        Assert.Equal(300, settings.ScanIntervalMs);
        Assert.Equal(5, settings.ConsecutiveMatches);
        Assert.Equal(0, settings.CooldownSeconds);
        Assert.False(settings.UseRegion);
    }

    [Fact]
    public async Task Monitor_TriggersAfterConsecutiveMatches_AndRearmsOnlyAfterMiss()
    {
        var image = new SequenceImageService(true, true, true, false, true, true);
        var service = new MonitorTriggerService(
            image,
            new FakeOcrService(),
            new FakeWindowService(),
            new FakeLogger());
        var profile = new RecordProfile
        {
            Name = "监控测试",
            Mode = AutomationMode.Monitor,
            MonitorSettings = new MonitorTriggerSettings
            {
                TriggerType = MonitorTriggerType.Image,
                ImagePath = typeof(MonitorTriggerServiceTests).Assembly.Location,
                ScanIntervalMs = 300,
                ConsecutiveMatches = 2,
                CooldownSeconds = 0
            }
        };

        var triggerCount = 0;
        var twoTriggers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Triggered += (_, _) =>
        {
            if (Interlocked.Increment(ref triggerCount) == 2)
                twoTriggers.TrySetResult();
        };

        await service.StartAsync(profile);
        await twoTriggers.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await service.StopAsync();

        Assert.Equal(2, triggerCount);
        Assert.True(image.ProbeCount >= 6);
    }

    [Fact]
    public async Task TestOnce_SupportsTextAndWindowTriggers()
    {
        var service = new MonitorTriggerService(
            new SequenceImageService(false),
            new FakeOcrService(found: true),
            new FakeWindowService(found: true),
            new FakeLogger());

        var text = await service.TestOnceAsync(
            new MonitorTriggerSettings { TriggerType = MonitorTriggerType.Text, TextPattern = "完成" },
            CancellationToken.None);
        var window = await service.TestOnceAsync(
            new MonitorTriggerSettings { TriggerType = MonitorTriggerType.Window, WindowTitle = "微信" },
            CancellationToken.None);

        Assert.True(text.Matched);
        Assert.True(window.Matched);
    }

    private sealed class SequenceImageService : IImageRecognitionService
    {
        private readonly Queue<bool> _results;
        private bool _last;
        public int ProbeCount { get; private set; }

        public SequenceImageService(params bool[] results)
        {
            _results = new Queue<bool>(results);
            _last = results.LastOrDefault();
        }

        public Task<(bool Found, int X, int Y, double Score)> FindImageAsync(
            string imagePath, double threshold, int timeoutMs, CancellationToken cancellationToken) =>
            FindImageOnceAsync(imagePath, threshold, null, cancellationToken);

        public Task<(bool Found, int X, int Y, double Score)> FindImageOnceAsync(
            string imagePath, double threshold, OcrRegion? region, CancellationToken cancellationToken,
            bool maskOwnWindows = true)
        {
            ProbeCount++;
            if (_results.Count > 0)
                _last = _results.Dequeue();
            return Task.FromResult((_last, 100, 100, _last ? 0.98 : 0.2));
        }
    }

    private sealed class FakeOcrService : IOcrService
    {
        private readonly bool _found;
        public FakeOcrService(bool found = false) => _found = found;

        public Task<OcrReadResult> RecognizeScreenAsync(OcrRegion? region, CancellationToken cancellationToken) =>
            Task.FromResult(new OcrReadResult(true, "完成", [], ""));

        public Task<OcrTextMatch> FindTextAsync(
            string text, int timeoutMs, OcrRegion? region, CancellationToken cancellationToken) =>
            FindTextOnceAsync(text, region, cancellationToken);

        public Task<OcrTextMatch> FindTextOnceAsync(
            string text, OcrRegion? region, CancellationToken cancellationToken,
            bool maskOwnWindows = true) =>
            Task.FromResult(new OcrTextMatch(_found, text, 0, 0, 20, 10, text, ""));
    }

    private sealed class FakeWindowService : IWindowAutomationService
    {
        private readonly bool _found;
        public FakeWindowService(bool found = false) => _found = found;
        public WindowSnapshot? GetForegroundWindowSnapshot() => null;
        public WindowSnapshot? GetWindowSnapshotAtPoint(int x, int y) => null;
        public bool TryResolvePoint(InputEvent evt, out int x, out int y)
        {
            x = evt.X;
            y = evt.Y;
            return false;
        }
        public bool ActivateWindow(string titleKeyword) => _found;
        public bool IsWindowAvailable(string titleKeyword) => _found;
        public Task<bool> WaitForWindowAsync(string titleKeyword, int timeoutMs, CancellationToken cancellationToken) =>
            Task.FromResult(_found);
    }

    private sealed class FakeLogger : IRunLogger
    {
        public string LogDirectory => "";
        public string ScreenshotDirectory => "";
        public string CurrentLogPath => "";
        public void Info(string source, string message) { }
        public void Warn(string source, string message) { }
        public void Error(string source, string message, Exception? exception = null, bool captureScreenshot = false) { }
        public string? CaptureScreenshot(string reason) => null;
    }
}
