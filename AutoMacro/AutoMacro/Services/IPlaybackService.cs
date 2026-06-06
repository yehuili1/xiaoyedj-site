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
    Task StartPlaybackAsync(IList<InputEvent> events, VariableTable variableTable, int loopCount, double playbackSpeed);
    void SetPlaybackSpeed(double playbackSpeed);
    void PausePlayback();
    void ResumePlayback();
    void StopPlayback();
}
