using SharpHook;

namespace AutoMacro.Services;

public class GlobalHookProvider : IGlobalHookProvider, IDisposable
{
    public SimpleGlobalHook Hook { get; }

    public GlobalHookProvider()
    {
        Hook = new SimpleGlobalHook();
    }

    public Task RunAsync()
    {
        return Hook.RunAsync();
    }

    public void Stop()
    {
        Hook.Dispose();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
