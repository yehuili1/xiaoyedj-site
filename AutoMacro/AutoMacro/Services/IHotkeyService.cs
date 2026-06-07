using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IHotkeyService
{
    HotkeySettings Settings { get; }
    event EventHandler? RecordHotkeyPressed;
    event EventHandler? PauseRecordHotkeyPressed;
    event EventHandler? StopHotkeyPressed;
    event EventHandler? PlaybackHotkeyPressed;
    event EventHandler? InsertRecordingHotkeyPressed;
    event EventHandler<string>? ProfilePlaybackRequested;
    void StartListening();
    void StopListening();
    void ReloadSettings();
}
