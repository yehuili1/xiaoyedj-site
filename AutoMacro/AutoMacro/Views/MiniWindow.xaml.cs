using System.Windows;
using System.Windows.Input;
using AutoMacro.ViewModels;

namespace AutoMacro.Views;

public partial class MiniWindow : Window
{
    public event EventHandler? RestoreRequested;

    public MiniWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // 默认显示在屏幕右下角，任务栏上方
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }
}
