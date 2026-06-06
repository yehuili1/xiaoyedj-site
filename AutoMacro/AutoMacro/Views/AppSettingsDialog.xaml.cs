using System.Windows;
using AutoMacro.Models;
using Wpf.Ui.Controls;

namespace AutoMacro.Views;

public partial class AppSettingsDialog : FluentWindow
{
    public AppSettings Settings { get; private set; }

    public AppSettingsDialog(AppSettings current)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            CloseBehavior = current.CloseBehavior,
            StartWithWindows = current.StartWithWindows
        };

        RbAsk.IsChecked = Settings.CloseBehavior == CloseBehavior.AskEveryTime;
        RbExit.IsChecked = Settings.CloseBehavior == CloseBehavior.ExitApp;
        RbMinimize.IsChecked = Settings.CloseBehavior == CloseBehavior.MinimizeToTray;
        CbStartWithWindows.IsChecked = Settings.StartWithWindows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.CloseBehavior = RbMinimize.IsChecked == true
            ? CloseBehavior.MinimizeToTray
            : RbExit.IsChecked == true
                ? CloseBehavior.ExitApp
                : CloseBehavior.AskEveryTime;
        Settings.StartWithWindows = CbStartWithWindows.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
