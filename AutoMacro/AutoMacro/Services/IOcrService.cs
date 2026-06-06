namespace AutoMacro.Services;

public sealed record OcrRegion(int X, int Y, int Width, int Height);

public sealed record OcrTextBox(string Text, int X, int Y, int Width, int Height);

public sealed record OcrReadResult(
    bool Success,
    string Text,
    IReadOnlyList<OcrTextBox> TextBoxes,
    string ErrorMessage);

public sealed record OcrTextMatch(
    bool Found,
    string Text,
    int X,
    int Y,
    int Width,
    int Height,
    string FullText,
    string ErrorMessage);

public interface IOcrService
{
    Task<OcrReadResult> RecognizeScreenAsync(
        OcrRegion? region,
        CancellationToken cancellationToken);

    Task<OcrTextMatch> FindTextAsync(
        string text,
        int timeoutMs,
        OcrRegion? region,
        CancellationToken cancellationToken);
}
