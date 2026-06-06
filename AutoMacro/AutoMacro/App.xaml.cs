using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using AutoMacro.Services;
using AutoMacro.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoMacro;

public partial class App : Application
{
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices((_, services) =>
        {
            // Services
            services.AddSingleton<IRunLogger, RunLogger>();
            services.AddSingleton<IGlobalHookProvider, GlobalHookProvider>();
            services.AddSingleton<IRecordingService, RecordingService>();
            services.AddSingleton<IPlaybackService, PlaybackService>();
            services.AddSingleton<IHotkeyService, HotkeyService>();
            services.AddSingleton<IProfileManager, ProfileManager>();
            services.AddSingleton<IClipboardInjector, ClipboardInjector>();
            services.AddSingleton<IWindowAutomationService, WindowAutomationService>();
            services.AddSingleton<IImageRecognitionService, ImageRecognitionService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<ActionListViewModel>();
            services.AddSingleton<VariableEditorViewModel>();

            // Windows
            services.AddSingleton<MainWindow>();
        })
        .Build();

    private static string CrashLogPath => Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
        "crash.log");

    public static IServiceProvider Services => _host.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        await _host.StartAsync();

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            Wpf.Ui.Appearance.ApplicationTheme.Dark);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI 未处理异常", e.Exception);
        System.Windows.MessageBox.Show(
            $"程序捕获到异常，详情已写入：\n{CrashLogPath}",
            "运行异常",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("非 UI 未处理异常", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("未观察到的任务异常", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string title, Exception exception)
    {
        try
        {
            var content = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}")
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80))
                .ToString();
            File.AppendAllText(CrashLogPath, content, Encoding.UTF8);

            try
            {
                _host.Services.GetService<IRunLogger>()
                    ?.Error("Crash", title, exception, captureScreenshot: true);
            }
            catch
            {
            }
        }
        catch
        {
        }
    }
}
