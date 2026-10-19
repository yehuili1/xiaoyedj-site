using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

public class WindowsOcrService : IOcrService
{
    private readonly IRunLogger _logger;

    public WindowsOcrService(IRunLogger logger)
    {
        _logger = logger;
    }

    public async Task<OcrReadResult> RecognizeScreenAsync(
        OcrRegion? region,
        CancellationToken cancellationToken)
    {
        return await RecognizeScreenCoreAsync(region, writeLog: true, cancellationToken);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private async Task<OcrReadResult> RecognizeScreenCoreAsync(
        OcrRegion? region,
        bool writeLog,
        CancellationToken cancellationToken,
        bool maskOwnWindows = true)
    {
        try
        {
            var engine = CreateEngine();
            if (engine is null)
                return FailedRead("本机 OCR 不可用，请确认 Windows 已安装中文识别语言包。");

            using var capture = CaptureScreen(region, maskOwnWindows);
            using var ocrBitmap = ResizeForOcr(capture.Bitmap, out var scale);
            using var softwareBitmap = BitmapToSoftwareBitmap(ocrBitmap);
            var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
            var textBoxes = BuildTextBoxes(result, capture.Left, capture.Top, scale);
            var text = result.Text?.Trim() ?? string.Empty;

            if (writeLog)
                _logger.Info("OCR", $"识别完成: length={text.Length}, boxes={textBoxes.Count}");
            return new OcrReadResult(true, text, textBoxes, string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (writeLog)
                _logger.Error("OCR", "识别文字失败", ex, captureScreenshot: true);
            return FailedRead(ex.Message);
        }
    }

    public async Task<OcrTextMatch> FindTextOnceAsync(
        string text,
        OcrRegion? region,
        CancellationToken cancellationToken,
        bool maskOwnWindows = true)
    {
        var keyword = text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return FailedMatch("要查找的文字不能为空。");

        var read = await RecognizeScreenCoreAsync(region, writeLog: false, cancellationToken, maskOwnWindows);
        if (!read.Success)
            return FailedMatch(read.ErrorMessage);

        var match = FindMatch(read, keyword);
        return match is null
            ? new OcrTextMatch(false, string.Empty, 0, 0, 0, 0, read.Text, string.Empty)
            : new OcrTextMatch(
                true,
                match.Text,
                match.X,
                match.Y,
                match.Width,
                match.Height,
                read.Text,
                string.Empty);
    }

    public async Task<OcrTextMatch> FindTextAsync(
        string text,
        int timeoutMs,
        OcrRegion? region,
        CancellationToken cancellationToken)
    {
        var keyword = text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return FailedMatch("要查找的文字不能为空。");

        var endAt = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        OcrReadResult? bestRead = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await RecognizeScreenAsync(region, cancellationToken);
            bestRead = read;

            if (!read.Success)
                return FailedMatch(read.ErrorMessage);

            var match = FindMatch(read, keyword);
            if (match is not null)
            {
                _logger.Info("OCR", $"找到文字: \"{keyword}\", x={match.X}, y={match.Y}, text=\"{match.Text}\"");
                return new OcrTextMatch(
                    true,
                    match.Text,
                    match.X,
                    match.Y,
                    match.Width,
                    match.Height,
                    read.Text,
                    string.Empty);
            }

            await Task.Delay(300, cancellationToken);
        } while (DateTime.UtcNow < endAt);

        _logger.Error("OCR", $"等待文字超时: \"{keyword}\"", captureScreenshot: true);
        return new OcrTextMatch(
            false,
            string.Empty,
            0,
            0,
            0,
            0,
            bestRead?.Text ?? string.Empty,
            $"没有找到文字：{keyword}");
    }

    private static OcrTextBox? FindMatch(OcrReadResult read, string keyword)
    {
        return read.TextBoxes.FirstOrDefault(box =>
            box.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static OcrEngine? CreateEngine()
    {
        var languages = OcrEngine.AvailableRecognizerLanguages;
        var zhHans = languages.FirstOrDefault(language =>
            language.LanguageTag.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            language.LanguageTag.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase));

        if (zhHans is not null)
            return OcrEngine.TryCreateFromLanguage(zhHans);

        var zh = languages.FirstOrDefault(language =>
            language.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));

        if (zh is not null)
            return OcrEngine.TryCreateFromLanguage(zh);

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static ScreenCapture CaptureScreen(OcrRegion? region, bool maskOwnWindows = true)
    {
        var virtualScreen = WinForms.SystemInformation.VirtualScreen;
        var left = region?.X ?? virtualScreen.Left;
        var top = region?.Y ?? virtualScreen.Top;
        var width = region is { Width: > 0 } ? region.Width : virtualScreen.Width;
        var height = region is { Height: > 0 } ? region.Height : virtualScreen.Height;

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
        if (maskOwnWindows)
            MaskOwnWindows(graphics, left, top, width, height);
        return new ScreenCapture(bitmap, left, top);
    }

    private static void MaskOwnWindows(Graphics graphics, int screenLeft, int screenTop, int screenWidth, int screenHeight)
    {
        var currentProcessId = (uint)Process.GetCurrentProcess().Id;
        var screenRect = new Rectangle(screenLeft, screenTop, screenWidth, screenHeight);
        using var brush = new HatchBrush(
            HatchStyle.WideDownwardDiagonal,
            Color.FromArgb(255, 255, 0, 255),
            Color.FromArgb(255, 0, 255, 0));

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            GetWindowThreadProcessId(handle, out var processId);
            if (processId != currentProcessId || !GetWindowRect(handle, out var rect))
                return true;

            var windowRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var clipped = Rectangle.Intersect(screenRect, windowRect);
            if (clipped.IsEmpty)
                return true;

            graphics.FillRectangle(
                brush,
                clipped.Left - screenLeft,
                clipped.Top - screenTop,
                clipped.Width,
                clipped.Height);
            return true;
        }, IntPtr.Zero);
    }

    private static Bitmap ResizeForOcr(Bitmap source, out double scale)
    {
        var maxDimension = Math.Max(1, OcrEngine.MaxImageDimension);
        var longest = Math.Max(source.Width, source.Height);
        scale = longest <= maxDimension ? 1.0 : (double)maxDimension / longest;

        if (scale >= 0.999)
            return CloneAs32Bpp(source);

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static Bitmap CloneAs32Bpp(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return clone;
    }

    private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            return SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static List<OcrTextBox> BuildTextBoxes(OcrResult result, int offsetX, int offsetY, double scale)
    {
        var boxes = new List<OcrTextBox>();
        var safeScale = scale <= 0 ? 1.0 : scale;

        foreach (var line in result.Lines)
        {
            var wordBoxes = line.Words.Select(word =>
            {
                var rect = word.BoundingRect;
                return new OcrTextBox(
                    word.Text,
                    offsetX + (int)Math.Round(rect.X / safeScale),
                    offsetY + (int)Math.Round(rect.Y / safeScale),
                    Math.Max(1, (int)Math.Round(rect.Width / safeScale)),
                    Math.Max(1, (int)Math.Round(rect.Height / safeScale)));
            }).ToList();

            if (wordBoxes.Count == 0)
                continue;

            var left = wordBoxes.Min(box => box.X);
            var top = wordBoxes.Min(box => box.Y);
            var right = wordBoxes.Max(box => box.X + box.Width);
            var bottom = wordBoxes.Max(box => box.Y + box.Height);
            boxes.Add(new OcrTextBox(
                line.Text,
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top)));
            boxes.AddRange(wordBoxes);
        }

        return boxes;
    }

    private static OcrReadResult FailedRead(string message) =>
        new(false, string.Empty, Array.Empty<OcrTextBox>(), message);

    private static OcrTextMatch FailedMatch(string message) =>
        new(false, string.Empty, 0, 0, 0, 0, string.Empty, message);

    private sealed record ScreenCapture(Bitmap Bitmap, int Left, int Top) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }
}
