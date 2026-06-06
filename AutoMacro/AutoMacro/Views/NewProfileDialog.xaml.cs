using Wpf.Ui.Controls;

namespace AutoMacro.Views;

public partial class NewProfileDialog : FluentWindow
{
    public string ProfileName => ProfileNameBox.Text.Trim();

    public NewProfileDialog(string? existingName = null)
    {
        InitializeComponent();

        if (existingName is not null)
        {
            Title = "重命名方案";
            ConfirmButton.Content = "确定";
            ProfileNameBox.Text = existingName;
        }

        ProfileNameBox.Focus();
    }

    private void Confirm_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
