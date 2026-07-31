using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IPlaybackService
{
    bool IsPlaying { get; }
    bool IsPaused { get; }
    int CurrentLoop { get; }
    event EventHandler? PlaybackStarted;
    event EventHandler? PlaybackStopped;
    event EventHandler? LoopChanged;
    event EventHandler<PlaybackStepChangedEventArgs>? StepChanged;
    event EventHandler<PlaybackDelayChangedEventArgs>? DelayChanged;
    Task StartPlaybackAsync(IList<InputEvent> events, VariableTable variableTable, int loopCount, double playbackSpeed, int startStepNumber = 1);
    void SetPlaybackSpeed(double playbackSpeed);
    void PausePlayback();
    void ResumePlayback();
    void StopPlayback();
}

public sealed class PlaybackDelayChangedEventArgs : EventArgs
{
    public PlaybackDelayChangedEventArgs(bool isActive, string label, int remainingMs, int totalMs)
    {
        IsActive = isActive;
        Label = label;
        RemainingMs = remainingMs;
        TotalMs = totalMs;
    }

    public bool IsActive { get; }
    public string Label { get; }
    public int RemainingMs { get; }
    public int TotalMs { get; }
}

public sealed class PlaybackStepChangedEventArgs : EventArgs
{
    public PlaybackStepChangedEventArgs(InputEvent action, int stepIndex, int totalSteps, int loop)
    {
        Action = action;
        StepIndex = stepIndex;
        TotalSteps = totalSteps;
        Loop = loop;
    }

    public InputEvent Action { get; }
    public int StepIndex { get; }
    public int TotalSteps { get; }
    public int Loop { get; }
}
