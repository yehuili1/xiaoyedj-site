using System.Windows;

namespace AutoMacro.Views;

public partial class CloseConfirmDialog : Window
{
    /// <summary>true = 最小化到托盘, false = 退出</summary>
    public bool MinimizeToTray => RbMinimize.IsChecked == true;

    /// <summary>用户是否勾选了"不再提示"</summary>
    public bool RememberChoice => CbRemember.IsChecked == true;

    public CloseConfirmDialog()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
