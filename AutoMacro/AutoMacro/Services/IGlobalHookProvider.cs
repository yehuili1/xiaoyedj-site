using SharpHook;

namespace AutoMacro.Services;

public interface IGlobalHookProvider
{
    SimpleGlobalHook Hook { get; }
    Task RunAsync();
    void Stop();
}
