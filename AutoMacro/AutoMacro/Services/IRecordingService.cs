using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IRecordingService
{
    bool IsRecording { get; }
    bool IsPaused { get; }
    event EventHandler<InputEvent>? EventRecorded;
    void StartRecording();
    void PauseRecording();
    void ResumeRecording();
    void StopRecording();
    List<InputEvent> GetRecordedEvents();
}
