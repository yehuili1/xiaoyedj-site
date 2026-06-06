using System.IO;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

public class ImageRecognitionService : IImageRecognitionService
{
    private readonly IRunLogger _logger;

    public ImageRecognitionService(IRunLogger logger)
    {
        _logger = logger;
    }

    public async Task<(bool Found, int X, int Y, double Score)> FindImageAsync(
        string imagePath,
        double threshold,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var normalizedThreshold = Math.Clamp(threshold <= 0 ? 0.92 : threshold, 0.5, 1.0);
        var endAt = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        if (!File.Exists(imagePath))
        {
            _logger.Error("Image", $"模板图片不存在: {imagePath}", captureScreenshot: true);
            return (false, 0, 0, 0);
        }

        using var template = new Bitmap(imagePath);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var screen = CaptureScreen();
            var result = FindInBitmap(screen, template, normalizedThreshold);
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

            await Task.Delay(250, cancellationToken);
        } while (DateTime.UtcNow < endAt);

        _logger.Error("Image", $"等待图片超时: {Path.GetFileName(imagePath)}, bestScore={bestScore:0.000}", captureScreenshot: true);
        return (false, bestX, bestY, bestScore);
    }

    private static Bitmap CaptureScreen()
    {
        var bounds = WinForms.SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        return bitmap;
    }

    private static (bool Found, int X, int Y, double Score) FindInBitmap(Bitmap screen, Bitmap template, double threshold)
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
                    bestX = x + template.Width / 2;
                    bestY = y + template.Height / 2;
                }

                if (score >= threshold)
                    return (true, x + template.Width / 2, y + template.Height / 2, score);
            }
        }

        return (false, bestX, bestY, bestScore);
    }

    private static List<Point> BuildSamplePoints(int width, int height)
    {
        var points = new List<Point>();
        var xSteps = Math.Min(12, Math.Max(3, width / 8));
        var ySteps = Math.Min(12, Math.Max(3, height / 8));

        for (var yi = 0; yi < ySteps; yi++)
        {
            var y = ySteps == 1 ? 0 : yi * (height - 1) / (ySteps - 1);
            for (var xi = 0; xi < xSteps; xi++)
            {
                var x = xSteps == 1 ? 0 : xi * (width - 1) / (xSteps - 1);
                points.Add(new Point(x, y));
            }
        }

        return points;
    }

    private static double ScoreAt(Bitmap screen, Bitmap template, int originX, int originY, List<Point> samplePoints)
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
}
