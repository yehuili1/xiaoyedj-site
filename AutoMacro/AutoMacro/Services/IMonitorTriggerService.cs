using AutoMacro.Models;

namespace AutoMacro.Services;

public sealed record MonitorProbeResult(
    bool Matched,
    string Message,
    double Score = 0);

public sealed class MonitorStatusChangedEventArgs : EventArgs
{
    public string Status { get; }
    public double? Score { get; }

    public MonitorStatusChangedEventArgs(string status, double? score = null)
    {
        Status = status;
        Score = score;
    }
}

public sealed class MonitorTriggeredEventArgs : EventArgs
{
    public RecordProfile Profile { get; }
    public MonitorProbeResult Result { get; }

    public MonitorTriggeredEventArgs(RecordProfile profile, MonitorProbeResult result)
    {
        Profile = profile;
        Result = result;
    }
}

public interface IMonitorTriggerService
{
    bool IsRunning { get; }
    RecordProfile? ActiveProfile { get; }
    event EventHandler<MonitorStatusChangedEventArgs>? StatusChanged;
    event EventHandler<MonitorTriggeredEventArgs>? Triggered;

    Task StartAsync(RecordProfile profile);
    Task StopAsync();
    void SetSuspended(bool suspended);
    Task<MonitorProbeResult> TestOnceAsync(
        MonitorTriggerSettings settings,
        CancellationToken cancellationToken,
        bool maskOwnWindows = true);
}
