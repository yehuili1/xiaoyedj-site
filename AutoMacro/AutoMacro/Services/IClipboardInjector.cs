namespace AutoMacro.Services;

public interface IClipboardInjector
{
    Task InjectTextAsync(string text, CancellationToken cancellationToken = default);
    Task CopyTextAsync(string text, CancellationToken cancellationToken = default);
}
