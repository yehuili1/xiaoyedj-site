namespace AutoMacro.Services;

public interface IImageRecognitionService
{
    Task<(bool Found, int X, int Y, double Score)> FindImageAsync(
        string imagePath,
        double threshold,
        int timeoutMs,
        CancellationToken cancellationToken);
}
