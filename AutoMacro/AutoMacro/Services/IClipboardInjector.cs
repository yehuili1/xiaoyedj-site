namespace AutoMacro.Services;

public interface IClipboardInjector
{
    Task InjectTextAsync(string text);
}
