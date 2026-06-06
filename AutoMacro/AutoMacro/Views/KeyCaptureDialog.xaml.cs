using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace AutoMacro.Views;

public partial class KeyCaptureDialog : FluentWindow
{
    public string? SelectedKeyCombo { get; private set; }

    public KeyCaptureDialog()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CapturedKeyBox.Focus();
        Keyboard.Focus(CapturedKeyBox);
    }

    private void CapturedKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = GetActualKey(e);
        if (IsModifierOnly(key))
        {
            StatusText.Text = "继续按主键，比如 A、F5、Enter。";
            e.Handled = true;
            return;
        }

        var mainKey = ConvertKeyToMainKeyString(key);
        if (mainKey is null)
        {
            StatusText.Text = "这个按键暂时不支持，请换一个常用按键。";
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(mainKey);

        SelectedKeyCombo = string.Join("+", parts);
        CapturedKeyBox.Text = SelectedKeyCombo;
        ConfirmButton.IsEnabled = true;
        StatusText.Text = "已识别，点确定添加到步骤里。";
        e.Handled = true;
    }

    private static Key GetActualKey(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
    }

    private static bool IsModifierOnly(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;
    }

    private static string? ConvertKeyToMainKeyString(Key key)
    {
        if (key >= Key.F1 && key <= Key.F12)
            return key.ToString();

        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num" + (key - Key.NumPad0);

        return key switch
        {
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Space => "Space",
            Key.Back => "Backspace",
            Key.CapsLock => "CapsLock",
            Key.Delete => "Delete",
            Key.Pause => "Pause",
            Key.Scroll => "ScrollLock",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.Multiply => "Num*",
            Key.Add => "Num+",
            Key.Subtract => "Num-",
            Key.Decimal => "Num.",
            Key.Divide => "Num/",
            _ => null
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedKeyCombo))
        {
            StatusText.Text = "请先按一个要执行的按键。";
            CapturedKeyBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
