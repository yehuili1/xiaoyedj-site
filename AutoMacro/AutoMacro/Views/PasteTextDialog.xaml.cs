using Wpf.Ui.Controls;

namespace AutoMacro.Views;

public partial class PasteTextDialog : FluentWindow
{
    public string TextContent => TextContentBox.Text;

    public PasteTextDialog(string? existingText = null)
    {
        InitializeComponent();
        TextContentBox.Text = existingText ?? string.Empty;
        TextContentBox.Focus();
    }

    private void Confirm_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextContent))
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
