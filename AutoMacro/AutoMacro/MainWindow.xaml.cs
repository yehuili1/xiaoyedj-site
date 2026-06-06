using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using AutoMacro.Services;
using AutoMacro.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMacro;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }
    private readonly TrayIconService _trayIcon;
    private Views.MiniWindow? _miniWindow;

    // 关闭行为记忆：null=未选择, true=最小化到托盘, false=退出
    private bool? _rememberedCloseAction;
    private bool _forceExit;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        ProjectTree.SelectedItemChanged += (_, e) =>
        {
            if (e.NewValue is TreeNodeViewModel node)
                ViewModel.SelectedTreeNode = node;
        };

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

        // 如果用户之前选了"不再提示"
        if (_rememberedCloseAction.HasValue)
        {
            e.Cancel = true;
            if (_rememberedCloseAction.Value)
                MinimizeToTray();
            else
                ForceExit();
            return;
        }

        // 弹出确认对话框
        e.Cancel = true;
        var dialog = new Views.CloseConfirmDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (dialog.RememberChoice)
                _rememberedCloseAction = dialog.MinimizeToTray;

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
}
