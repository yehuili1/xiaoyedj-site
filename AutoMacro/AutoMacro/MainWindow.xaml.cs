using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using AutoMacro.Models;
using AutoMacro.Services;
using AutoMacro.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMacro;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }
    private readonly TrayIconService _trayIcon;
    private Views.MiniWindow? _miniWindow;

    private AppSettings _appSettings = AppSettings.Load();
    private bool _forceExit;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // 托盘图标
        _trayIcon = new TrayIconService();
        _trayIcon.ShowWindowRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _trayIcon.ExitRequested += (_, _) => Dispatcher.Invoke(ForceExit);
        _trayIcon.ToggleRecordRequested += (_, _) => Dispatcher.Invoke(() => ViewModel.ToggleRecordingCommand.Execute(null));
        _trayIcon.TogglePlaybackRequested += (_, _) => Dispatcher.Invoke(() => ViewModel.TogglePlaybackCommand.Execute(null));
        _trayIcon.StopRequested += (_, _) => Dispatcher.Invoke(() => ViewModel.EmergencyStopCommand.Execute(null));
        _trayIcon.MiniModeRequested += (_, _) => Dispatcher.Invoke(SwitchToMiniMode);

        // 监听状态变化更新托盘图标
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_forceExit)
        {
            _trayIcon.Dispose();
            return;
        }

        if (_appSettings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        if (_appSettings.CloseBehavior == CloseBehavior.ExitApp)
        {
            e.Cancel = true;
            ForceExit();
            return;
        }

        // 弹出确认对话框
        e.Cancel = true;
        var dialog = new Views.CloseConfirmDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (dialog.RememberChoice)
            {
                _appSettings.CloseBehavior = dialog.MinimizeToTray
                    ? CloseBehavior.MinimizeToTray
                    : CloseBehavior.ExitApp;
                _appSettings.Save();
            }

            if (dialog.MinimizeToTray)
                MinimizeToTray();
            else
                ForceExit();
        }
    }

    private void MinimizeToTray()
    {
        Hide();
    }

    private void ForceExit()
    {
        _forceExit = true;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsRecording) or nameof(MainViewModel.IsPlaying))
        {
            _trayIcon.SetState(ViewModel.IsRecording, ViewModel.IsPlaying);
        }
    }

    private void RestoreFromTray()
    {
        if (_miniWindow is not null)
        {
            _miniWindow.Close();
            _miniWindow = null;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void SwitchToMiniMode()
    {
        Hide();
        _miniWindow = new Views.MiniWindow(ViewModel);
        _miniWindow.RestoreRequested += (_, _) =>
        {
            _miniWindow.Close();
            _miniWindow = null;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _miniWindow.Show();
    }

    private void MiniMode_Click(object sender, RoutedEventArgs e)
    {
        SwitchToMiniMode();
    }

    private void AppSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.AppSettingsDialog(_appSettings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        _appSettings = dialog.Settings;
        try
        {
            _appSettings.ApplyStartupSetting();
            _appSettings.Save();
            System.Windows.MessageBox.Show(
                "软件设置已保存。",
                "设置成功",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "设置保存失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
