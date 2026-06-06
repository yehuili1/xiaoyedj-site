namespace AutoMacro.Services;

public interface IRunLogger
{
    string LogDirectory { get; }
    string ScreenshotDirectory { get; }
    string CurrentLogPath { get; }
    void Info(string source, string message);
    void Warn(string source, string message);
    void Error(string source, string message, Exception? exception = null, bool captureScreenshot = false);
    string? CaptureScreenshot(string reason);
}
