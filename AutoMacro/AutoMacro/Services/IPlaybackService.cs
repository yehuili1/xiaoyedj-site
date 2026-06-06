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
    Task StartPlaybackAsync(IList<InputEvent> events, VariableTable variableTable, int loopCount, double playbackSpeed);
    void SetPlaybackSpeed(double playbackSpeed);
    void PausePlayback();
    void ResumePlayback();
    void StopPlayback();
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
