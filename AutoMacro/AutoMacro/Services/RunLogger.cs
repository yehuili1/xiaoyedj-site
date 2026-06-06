using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

public class RunLogger : IRunLogger
{
    private readonly object _syncRoot = new();

    public string LogDirectory { get; }
    public string ScreenshotDirectory { get; }
    public string CurrentLogPath { get; }

    public RunLogger()
    {
        var baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        LogDirectory = Path.Combine(baseDir, "Logs");
        ScreenshotDirectory = Path.Combine(LogDirectory, "Screenshots");
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ScreenshotDirectory);
        CurrentLogPath = Path.Combine(LogDirectory, $"run_{DateTime.Now:yyyyMMdd}.log");

        Info("App", $"日志启动: {Environment.ProcessPath}");
    }

    public void Info(string source, string message) => Write("INFO", source, message);

    public void Warn(string source, string message) => Write("WARN", source, message);

    public void Error(string source, string message, Exception? exception = null, bool captureScreenshot = false)
    {
        string? screenshotPath = null;
        if (captureScreenshot)
            screenshotPath = CaptureScreenshot(source);

        var detail = new StringBuilder(message);
        if (screenshotPath is not null)
            detail.AppendLine().Append("Screenshot: ").Append(screenshotPath);
        if (exception is not null)
            detail.AppendLine().Append(exception);

        Write("ERROR", source, detail.ToString());
    }

    public string? CaptureScreenshot(string reason)
    {
        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            var bounds = WinForms.SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(reason)}.png";
            var path = Path.Combine(ScreenshotDirectory, fileName);
            bitmap.Save(path, ImageFormat.Png);
            Write("INFO", "Screenshot", path);
            return path;
        }
        catch (Exception ex)
        {
            Write("ERROR", "Screenshot", $"截图失败: {ex}");
            return null;
        }
    }

    private void Write(string level, string source, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{source}] {message}{Environment.NewLine}";
            lock (_syncRoot)
            {
                File.AppendAllText(CurrentLogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        cleaned = cleaned.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "screenshot";

        return cleaned.Length <= 60 ? cleaned : cleaned[..60];
    }
}
