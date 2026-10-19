using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using DrawingPoint = System.Drawing.Point;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

public class ImageRecognitionService : IImageRecognitionService
{
    private static readonly double[] TemplateScales = { 1.0, 0.98, 1.02, 0.95, 1.05, 0.92, 1.08, 0.9, 1.1, 0.85, 1.15, 0.8, 1.2, 0.75, 1.25 };
    private static readonly double[] BalancedTemplateScales = { 1.0, 0.98, 1.02, 0.95, 1.05, 0.92, 1.08 };
    private static readonly double[] QuickTemplateScales = { 1.0, 0.98, 1.02 };

    private readonly IRunLogger _logger;
    private bool _openCvFallbackLogged;
    private readonly object _templateCacheLock = new();
    private string? _cachedTemplatePath;
    private DateTime _cachedTemplateLastWrite;
    private Bitmap? _cachedTemplate;

    public ImageRecognitionService(IRunLogger logger)
    {
        _logger = logger;
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

    public async Task<(bool Found, int X, int Y, double Score)> FindImageAsync(
        string imagePath,
        double threshold,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var normalizedThreshold = Math.Clamp(threshold <= 0 ? 0.92 : threshold, 0.5, 1.0);
        var timeout = Math.Max(0, timeoutMs);
        var endAt = DateTime.UtcNow.AddMilliseconds(timeout);
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        if (!File.Exists(imagePath))
        {
            _logger.Error("Image", $"模板图片不存在: {imagePath}", captureScreenshot: true);
            return (false, 0, 0, 0);
        }

        using var templateSource = new Bitmap(imagePath);
        using var template = CloneAs24Bpp(templateSource);
        var isColorRichTemplate = IsColorRichIconTemplate(template);
        var effectiveThreshold = GetEffectiveThreshold(normalizedThreshold, template, isColorRichTemplate);
        var templateScales = GetTemplateScales(timeout);
        if (effectiveThreshold < normalizedThreshold)
        {
            _logger.Info(
                "Image",
                $"小截图自动放宽匹配: {Path.GetFileName(imagePath)}, size={template.Width}x{template.Height}, threshold={normalizedThreshold:0.00}->{effectiveThreshold:0.00}");
        }

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = CaptureScreen();
            using var screen = capture.Bitmap;
            var result = FindInBitmap(screen, template, effectiveThreshold, capture.Left, capture.Top, templateScales, isColorRichTemplate);
            if (result.Found)
            {
                _logger.Info("Image", $"找到图片: {Path.GetFileName(imagePath)}, score={result.Score:0.000}, x={result.X}, y={result.Y}");
                return result;
            }

            if (result.Score > bestScore)
            {
                bestScore = result.Score;
                bestX = result.X;
                bestY = result.Y;
            }

            var remainingMs = (int)Math.Ceiling((endAt - DateTime.UtcNow).TotalMilliseconds);
            if (remainingMs <= 0)
                break;

            await Task.Delay(Math.Min(100, remainingMs), cancellationToken);
        } while (DateTime.UtcNow < endAt);

        _logger.Warn("Image", $"等待图片超时: {Path.GetFileName(imagePath)}, bestScore={bestScore:0.000}, threshold={effectiveThreshold:0.000}, bestX={bestX}, bestY={bestY}");
        _logger.CaptureScreenshot($"ImageMiss_{Path.GetFileNameWithoutExtension(imagePath)}");
        return (false, bestX, bestY, bestScore);
    }

    public Task<(bool Found, int X, int Y, double Score)> FindImageOnceAsync(
        string imagePath,
        double threshold,
        OcrRegion? region,
        CancellationToken cancellationToken,
        bool maskOwnWindows = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(imagePath))
            return Task.FromResult((false, 0, 0, 0d));

        try
        {
            using var template = LoadCachedTemplateClone(imagePath);
            var capture = CaptureScreen(region, maskOwnWindows);
            using var screen = capture.Bitmap;
            var normalizedThreshold = Math.Clamp(threshold <= 0 ? 0.92 : threshold, 0.5, 1.0);
            var isColorRichTemplate = IsColorRichIconTemplate(template);
            var effectiveThreshold = GetEffectiveThreshold(normalizedThreshold, template, isColorRichTemplate);
            var result = FindInBitmap(
                screen,
                template,
                effectiveThreshold,
                capture.Left,
                capture.Top,
                QuickTemplateScales,
                isColorRichTemplate);
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Monitor", $"静默识图失败: {ex.Message}");
            return Task.FromResult((false, 0, 0, 0d));
        }
    }

    private Bitmap LoadCachedTemplateClone(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath);
        var lastWrite = File.GetLastWriteTimeUtc(fullPath);

        lock (_templateCacheLock)
        {
            if (_cachedTemplate is null ||
                !string.Equals(_cachedTemplatePath, fullPath, StringComparison.OrdinalIgnoreCase) ||
                _cachedTemplateLastWrite != lastWrite)
            {
                _cachedTemplate?.Dispose();
                using var source = new Bitmap(fullPath);
                _cachedTemplate = CloneAs24Bpp(source);
                _cachedTemplatePath = fullPath;
                _cachedTemplateLastWrite = lastWrite;
            }

            return CloneAs24Bpp(_cachedTemplate);
        }
    }

    private static double[] GetTemplateScales(int timeoutMs)
    {
        if (timeoutMs <= 1200)
            return QuickTemplateScales;

        if (timeoutMs <= 5000)
            return BalancedTemplateScales;

        return TemplateScales;
    }

    private static double GetEffectiveThreshold(double threshold, Bitmap template, bool isColorRichTemplate)
    {
        var templateWidth = template.Width;
        var templateHeight = template.Height;
        var area = templateWidth * templateHeight;
        if (isColorRichTemplate)
        {
            if (IsWideColorListItemTemplate(templateWidth, templateHeight))
                return Math.Min(threshold, 0.86);

            if (area <= 22000 && templateHeight <= 140)
                return Math.Min(threshold, 0.88);

            if (area <= 36000 && templateHeight <= 180)
                return Math.Min(threshold, 0.89);
        }

        if (area <= 2500 || templateHeight <= 32)
            return Math.Min(threshold, 0.88);

        if (area <= 6000 || templateHeight <= 48)
            return Math.Min(threshold, 0.90);

        if (area <= 12000 || templateHeight <= 64)
            return Math.Min(threshold, 0.90);

        return threshold;
    }

    private static bool IsWideColorListItemTemplate(int templateWidth, int templateHeight)
    {
        return templateWidth >= 160 &&
               templateHeight >= 60 &&
               templateWidth / (double)templateHeight >= 2.2;
    }

    private static bool IsColorRichIconTemplate(Bitmap template)
    {
        var total = 0;
        var colorful = 0;
        var spreadTotal = 0d;
        var stepX = Math.Max(1, template.Width / 24);
        var stepY = Math.Max(1, template.Height / 24);

        for (var y = 0; y < template.Height; y += stepY)
        {
            for (var x = 0; x < template.Width; x += stepX)
            {
                var color = template.GetPixel(x, y);
                var max = Math.Max(color.R, Math.Max(color.G, color.B));
                var min = Math.Min(color.R, Math.Min(color.G, color.B));
                var spread = max - min;
                spreadTotal += spread;
                if (spread >= 32 && max >= 50)
                    colorful++;

                total++;
            }
        }

        if (total == 0)
            return false;

        return spreadTotal / total >= 18 || (double)colorful / total >= 0.12;
    }

    private (bool Found, int X, int Y, double Score) FindInBitmap(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY,
        double[] templateScales,
        bool allowPartialTemplates)
    {
        try
        {
            return FindWithOpenCv(screen, template, threshold, offsetX, offsetY, templateScales, allowPartialTemplates);
        }
        catch (Exception ex)
        {
            if (!_openCvFallbackLogged)
            {
                _openCvFallbackLogged = true;
                _logger.Warn("Image", $"OpenCV 图片识别不可用，已切换到兼容算法: {ex.Message}");
            }

            return FindWithSampling(screen, template, threshold, offsetX, offsetY);
        }
    }

    private static ScreenCapture CaptureScreen(OcrRegion? region = null, bool maskOwnWindows = true)
    {
        var virtualBounds = WinForms.SystemInformation.VirtualScreen;
        var requested = region is { Width: > 0, Height: > 0 }
            ? new Rectangle(region.X, region.Y, region.Width, region.Height)
            : virtualBounds;
        var bounds = Rectangle.Intersect(virtualBounds, requested);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = virtualBounds;

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        if (maskOwnWindows)
            MaskOwnWindows(graphics, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return new ScreenCapture(bitmap, bounds.Left, bounds.Top);
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
            if (processId != currentProcessId)
                return true;

            if (!GetWindowRect(handle, out var rect))
                return true;

            var windowRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (windowRect.Width < 20 || windowRect.Height < 20)
                return true;

            var clipped = Rectangle.Intersect(screenRect, windowRect);
            if (clipped.IsEmpty)
                return true;

            var local = new Rectangle(
                clipped.Left - screenLeft,
                clipped.Top - screenTop,
                clipped.Width,
                clipped.Height);
            graphics.FillRectangle(brush, local);
            return true;
        }, IntPtr.Zero);
    }

    private (bool Found, int X, int Y, double Score) FindWithOpenCv(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY,
        double[] templateScales,
        bool allowPartialTemplates)
    {
        using var screenColor = BitmapToMat(screen);
        using var templateColor = BitmapToMat(template);
        using var screenGray = new Mat();
        using var templateGray = new Mat();

        Cv2.CvtColor(screenColor, screenGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(templateColor, templateGray, ColorConversionCodes.BGR2GRAY);

        if (templateGray.Width > screenGray.Width || templateGray.Height > screenGray.Height)
            return (false, 0, 0, 0);

        if (IsLowVarianceTemplate(templateGray))
        {
            var lowVarianceEdgeResult = FindWithEdges(screenGray, templateGray, threshold, offsetX, offsetY, templateScales);
            if (lowVarianceEdgeResult.Found)
                return lowVarianceEdgeResult;

            WarnOpenCvFallback("模板图片细节太少，OpenCV 匹配容易误判，已切换到兼容算法。建议截取包含文字、图标或边框的图片。");
            return FindWithSampling(screen, template, threshold, offsetX, offsetY);
        }

        var bestResult = FindWithGrayScales(screenGray, templateGray, threshold, offsetX, offsetY, templateScales);
        if (bestResult.Found)
            return bestResult;

        var edgeResult = FindWithEdges(screenGray, templateGray, threshold, offsetX, offsetY, templateScales);
        if (edgeResult.Found)
            return edgeResult;

        var partialResult = allowPartialTemplates
            ? FindWithPartialTemplates(screenGray, templateGray, threshold, offsetX, offsetY, templateScales)
            : (false, 0, 0, 0);
        if (partialResult.Found)
            return partialResult;

        var bestMiss = edgeResult.Score > bestResult.Score ? edgeResult : bestResult;
        return partialResult.Score > bestMiss.Score ? partialResult : bestMiss;
    }

    private static (bool Found, int X, int Y, double Score) FindWithGrayScales(
        Mat screenGray,
        Mat templateGray,
        double threshold,
        int offsetX,
        int offsetY,
        double[] templateScales)
    {
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;
        foreach (var scale in templateScales)
        {
            var width = Math.Max(1, (int)Math.Round(templateGray.Width * scale));
            var height = Math.Max(1, (int)Math.Round(templateGray.Height * scale));
            if (width > screenGray.Width || height > screenGray.Height)
                continue;

            Mat candidate = templateGray;
            Mat? resized = null;
            try
            {
                if (Math.Abs(scale - 1.0) > 0.001)
                {
                    resized = new Mat();
                    Cv2.Resize(
                        templateGray,
                        resized,
                        new OpenCvSharp.Size(width, height),
                        0,
                        0,
                        scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Linear);
                    candidate = resized;
                }

                using var result = new Mat();
                Cv2.MatchTemplate(screenGray, candidate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

                if (double.IsNaN(maxValue) || double.IsInfinity(maxValue))
                    maxValue = 0;

                var verifiedScore = VerifyGrayContentScore(screenGray, candidate, maxLocation, maxValue);

                if (verifiedScore > bestScore)
                {
                    bestScore = verifiedScore;
                    bestX = offsetX + maxLocation.X + candidate.Width / 2;
                    bestY = offsetY + maxLocation.Y + candidate.Height / 2;
                }

                if (verifiedScore >= GetEarlyReturnThreshold(threshold))
                    return (true, offsetX + maxLocation.X + candidate.Width / 2, offsetY + maxLocation.Y + candidate.Height / 2, verifiedScore);
            }
            finally
            {
                resized?.Dispose();
            }
        }

        return (bestScore >= threshold, bestX, bestY, bestScore);
    }

    private static (bool Found, int X, int Y, double Score) FindWithPartialTemplates(
        Mat screenGray,
        Mat templateGray,
        double threshold,
        int offsetX,
        int offsetY,
        double[] templateScales)
    {
        if (!ShouldTryPartialTemplates(templateGray))
            return (false, 0, 0, 0);

        var partialThreshold = GetPartialTemplateThreshold(threshold, templateGray.Width, templateGray.Height);
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        foreach (var crop in BuildPartialTemplateCrops(templateGray.Width, templateGray.Height))
        {
            using var sourceCrop = new Mat(templateGray, crop);
            if (IsLowVarianceTemplate(sourceCrop))
                continue;

            foreach (var scale in templateScales)
            {
                var width = Math.Max(1, (int)Math.Round(crop.Width * scale));
                var height = Math.Max(1, (int)Math.Round(crop.Height * scale));
                if (width > screenGray.Width || height > screenGray.Height)
                    continue;

                using var candidate = ResizeTemplate(sourceCrop, width, height, scale);
                using var result = new Mat();
                Cv2.MatchTemplate(screenGray, candidate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

                if (double.IsNaN(maxValue) || double.IsInfinity(maxValue))
                    maxValue = 0;

                var verifiedScore = VerifyGrayContentScore(screenGray, candidate, maxLocation, maxValue);
                var scaledCropX = (int)Math.Round(crop.X * scale);
                var scaledCropY = (int)Math.Round(crop.Y * scale);
                var scaledTemplateWidth = (int)Math.Round(templateGray.Width * scale);
                var scaledTemplateHeight = (int)Math.Round(templateGray.Height * scale);
                var clickX = offsetX + maxLocation.X - scaledCropX + scaledTemplateWidth / 2;
                var clickY = offsetY + maxLocation.Y - scaledCropY + scaledTemplateHeight / 2;

                if (verifiedScore > bestScore)
                {
                    bestScore = verifiedScore;
                    bestX = clickX;
                    bestY = clickY;
                }

                if (verifiedScore >= partialThreshold)
                    return (true, clickX, clickY, verifiedScore);
            }
        }

        return (false, bestX, bestY, bestScore);
    }

    private static bool ShouldTryPartialTemplates(Mat templateGray)
    {
        var area = templateGray.Width * templateGray.Height;
        return templateGray.Width >= 48 &&
               templateGray.Height >= 28 &&
               area <= 36000;
    }

    private static double GetPartialTemplateThreshold(double threshold, int templateWidth, int templateHeight)
    {
        var area = templateWidth * templateHeight;
        if (area <= 6000 || templateHeight <= 48)
            return Math.Max(0.86, threshold - 0.04);

        return Math.Max(0.88, threshold - 0.03);
    }

    private static IEnumerable<OpenCvSharp.Rect> BuildPartialTemplateCrops(int width, int height)
    {
        var minWidth = Math.Max(24, width / 3);
        var minHeight = Math.Max(16, height / 3);

        var rightX = Math.Max(8, width / 4);
        if (width - rightX >= minWidth)
            yield return new OpenCvSharp.Rect(rightX, 0, width - rightX, height);

        var leftWidth = width - Math.Max(8, width / 4);
        if (leftWidth >= minWidth)
            yield return new OpenCvSharp.Rect(0, 0, leftWidth, height);

        var centerX = width / 8;
        var centerY = height / 8;
        var centerWidth = width - centerX * 2;
        var centerHeight = height - centerY * 2;
        if (centerWidth >= minWidth && centerHeight >= minHeight)
            yield return new OpenCvSharp.Rect(centerX, centerY, centerWidth, centerHeight);

        var bottomY = height / 2;
        if (height - bottomY >= minHeight)
            yield return new OpenCvSharp.Rect(0, bottomY, width, height - bottomY);
    }

    private static (bool Found, int X, int Y, double Score) FindWithEdges(
        Mat screenGray,
        Mat templateGray,
        double threshold,
        int offsetX,
        int offsetY,
        double[] templateScales)
    {
        var edgeThreshold = GetEdgeThreshold(threshold, templateGray.Width, templateGray.Height);
        var contentThreshold = GetEdgeContentThreshold(threshold, templateGray.Width, templateGray.Height);
        using var screenEdges = BuildEdges(screenGray);
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        foreach (var scale in templateScales)
        {
            var width = Math.Max(1, (int)Math.Round(templateGray.Width * scale));
            var height = Math.Max(1, (int)Math.Round(templateGray.Height * scale));
            if (width > screenGray.Width || height > screenGray.Height)
                continue;

            using var candidateGray = ResizeTemplate(templateGray, width, height, scale);
            using var candidateEdges = BuildEdges(candidateGray);
            if (!HasEnoughEdges(candidateEdges))
                continue;

            using var result = new Mat();
            Cv2.MatchTemplate(screenEdges, candidateEdges, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

            if (double.IsNaN(maxValue) || double.IsInfinity(maxValue))
                maxValue = 0;

            var contentScore = VerifyGrayContentScore(screenGray, candidateGray, maxLocation, maxValue);
            var verifiedScore = Math.Min(maxValue, contentScore);

            if (verifiedScore > bestScore)
            {
                bestScore = verifiedScore;
                bestX = offsetX + maxLocation.X + candidateEdges.Width / 2;
                bestY = offsetY + maxLocation.Y + candidateEdges.Height / 2;
            }

            if (maxValue >= edgeThreshold && contentScore >= contentThreshold)
                return (true, bestX, bestY, verifiedScore);
        }

        return (false, bestX, bestY, bestScore);
    }

    private static Mat ResizeTemplate(Mat templateGray, int width, int height, double scale)
    {
        if (Math.Abs(scale - 1.0) <= 0.001)
            return templateGray.Clone();

        var resized = new Mat();
        Cv2.Resize(
            templateGray,
            resized,
            new OpenCvSharp.Size(width, height),
            0,
            0,
            scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Linear);
        return resized;
    }

    private static Mat BuildEdges(Mat gray)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(3, 3), 0);

        var edges = new Mat();
        Cv2.Canny(blurred, edges, 45, 135);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
        Cv2.Dilate(edges, edges, kernel);
        return edges;
    }

    private static bool HasEnoughEdges(Mat edges)
    {
        var edgePixels = Cv2.CountNonZero(edges);
        var minEdges = Math.Max(10, (int)Math.Round(edges.Width * edges.Height * 0.008));
        return edgePixels >= minEdges;
    }

    private static double GetEdgeThreshold(double threshold, int templateWidth, int templateHeight)
    {
        var area = templateWidth * templateHeight;
        if (area <= 2500 || templateHeight <= 32)
            return Math.Max(0.86, threshold - 0.02);

        if (area <= 6000 || templateHeight <= 48)
            return Math.Max(0.88, threshold - 0.02);

        return Math.Max(0.90, threshold - 0.02);
    }

    private static double GetEdgeContentThreshold(double threshold, int templateWidth, int templateHeight)
    {
        var area = templateWidth * templateHeight;
        if (area <= 2500 || templateHeight <= 32)
            return Math.Max(0.86, threshold - 0.03);

        if (area <= 6000 || templateHeight <= 48)
            return Math.Max(0.88, threshold - 0.03);

        return Math.Max(0.89, threshold - 0.03);
    }

    private static double GetEarlyReturnThreshold(double threshold)
    {
        if (threshold >= 0.9)
            return threshold;

        return Math.Min(0.92, Math.Max(threshold, threshold + 0.08));
    }

    private static double VerifyGrayContentScore(
        Mat screenGray,
        Mat candidateGray,
        OpenCvSharp.Point location,
        double correlationScore)
    {
        if (location.X < 0 ||
            location.Y < 0 ||
            location.X + candidateGray.Width > screenGray.Width ||
            location.Y + candidateGray.Height > screenGray.Height)
        {
            return 0;
        }

        using var patch = new Mat(
            screenGray,
            new OpenCvSharp.Rect(location.X, location.Y, candidateGray.Width, candidateGray.Height));
        using var diff = new Mat();
        Cv2.Absdiff(patch, candidateGray, diff);
        var meanDiff = Cv2.Mean(diff).Val0;
        var differenceScore = Math.Clamp(1.0 - meanDiff / 255.0, 0.0, 1.0);

        if (double.IsNaN(correlationScore) || double.IsInfinity(correlationScore))
            return differenceScore;

        return Math.Min(Math.Clamp(correlationScore, 0.0, 1.0), differenceScore);
    }

    private void WarnOpenCvFallback(string message)
    {
        if (_openCvFallbackLogged)
            return;

        _openCvFallbackLogged = true;
        _logger.Warn("Image", message);
    }

    private static bool IsLowVarianceTemplate(Mat templateGray)
    {
        Cv2.MeanStdDev(templateGray, out _, out var stddev);
        return stddev.Val0 < 3.0;
    }

    private static Mat BitmapToMat(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            using var wrapped = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap CloneAs24Bpp(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return clone;
    }

    private static (bool Found, int X, int Y, double Score) FindWithSampling(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY)
    {
        if (template.Width > screen.Width || template.Height > screen.Height)
            return (false, 0, 0, 0);

        var samplePoints = BuildSamplePoints(template.Width, template.Height);
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;
        var scanStep = template.Width * template.Height > 9000 ? 3 : 2;

        for (var y = 0; y <= screen.Height - template.Height; y += scanStep)
        {
            for (var x = 0; x <= screen.Width - template.Width; x += scanStep)
            {
                var score = ScoreAt(screen, template, x, y, samplePoints);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = offsetX + x + template.Width / 2;
                    bestY = offsetY + y + template.Height / 2;
                }

                if (score >= threshold)
                    return (true, offsetX + x + template.Width / 2, offsetY + y + template.Height / 2, score);
            }
        }

        return (false, bestX, bestY, bestScore);
    }

    private static List<DrawingPoint> BuildSamplePoints(int width, int height)
    {
        var points = new List<DrawingPoint>();
        var xSteps = Math.Min(12, Math.Max(3, width / 8));
        var ySteps = Math.Min(12, Math.Max(3, height / 8));

        for (var yi = 0; yi < ySteps; yi++)
        {
            var y = ySteps == 1 ? 0 : yi * (height - 1) / (ySteps - 1);
            for (var xi = 0; xi < xSteps; xi++)
            {
                var x = xSteps == 1 ? 0 : xi * (width - 1) / (xSteps - 1);
                points.Add(new DrawingPoint(x, y));
            }
        }

        return points;
    }

    private static double ScoreAt(Bitmap screen, Bitmap template, int originX, int originY, List<DrawingPoint> samplePoints)
    {
        double total = 0;
        foreach (var point in samplePoints)
        {
            var a = screen.GetPixel(originX + point.X, originY + point.Y);
            var b = template.GetPixel(point.X, point.Y);
            var distance = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            total += 1.0 - distance / 765.0;
        }

        return total / samplePoints.Count;
    }

    private sealed record ScreenCapture(Bitmap Bitmap, int Left, int Top);
}
