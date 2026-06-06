using System.Windows;
using System.Windows.Input;
using AutoMacro.Models;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using TextBox = Wpf.Ui.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoMacro.Views;

/// <summary>
/// 方案快捷键绑定项（用于 ItemsControl 数据绑定）
/// </summary>
public class ProfileHotkeyEntry
{
    public string ProfileName { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

public partial class HotkeySettingsDialog : FluentWindow
{
    public HotkeySettings? Result { get; private set; }
    private readonly List<ProfileHotkeyEntry> _profileEntries = new();

    public HotkeySettingsDialog(HotkeySettings current, IEnumerable<string> profileNames)
    {
        InitializeComponent();

        TbRecord.Text = current.StartStopRecording;
        TbPause.Text = current.PauseRecording;
        TbStop.Text = current.EmergencyStop;
        TbPlayback.Text = current.StartPlayback;

        // 填充方案快捷键列表
        foreach (var name in profileNames)
        {
            current.ProfileHotkeys.TryGetValue(name, out var hotkey);
            _profileEntries.Add(new ProfileHotkeyEntry
            {
                ProfileName = name,
                Hotkey = hotkey ?? ""
            });
        }
        ProfileHotkeyList.ItemsSource = _profileEntries;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 只按了修饰键本身，不做任何操作（等待主键）
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        // 忽略无意义的按键
        if (key is Key.Tab or Key.Escape or Key.Enter)
        {
            e.Handled = true;
            return;
        }

        var mainKey = ConvertKeyToMainKeyString(key);
        if (mainKey is null)
        {
            e.Handled = true;
            return;
        }

        // 构建组合键字符串
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(mainKey);

        textBox.Text = string.Join("+", parts);
        e.Handled = true;
    }

    /// <summary>
    /// 将 WPF Key 转为主键显示字符串，支持任意按键
    /// </summary>
    private static string? ConvertKeyToMainKeyString(Key key)
    {
        // F1-F12
        if (key >= Key.F1 && key <= Key.F12)
            return key.ToString();

        // A-Z
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        // 0-9 (主键盘)
        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        // 小键盘 0-9
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num" + (key - Key.NumPad0);

        return key switch
        {
            Key.Space => "Space",
            Key.Back => "Backspace",
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = new[]
        {
            TbRecord.Text?.Trim(),
            TbPause.Text?.Trim(),
            TbStop.Text?.Trim(),
            TbPlayback.Text?.Trim()
        };

        // 验证非空的快捷键是否合法
        var nonEmpty = selected.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (nonEmpty.Any(s => !HotkeySettings.IsSupportedHotkey(s!)))
        {
            MessageBox.Show(
                "存在不支持的快捷键，请重新设置。",
                "快捷键无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var distinct = nonEmpty.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinct < nonEmpty.Length)
        {
            MessageBox.Show(
                "不同功能不能使用相同的快捷键，请修改重复项。",
                "快捷键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 收集方案快捷键
        var profileHotkeys = new Dictionary<string, string>();
        var allHotkeys = new List<string>(selected!);

        foreach (var entry in _profileEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Hotkey)) continue;

            if (!HotkeySettings.IsSupportedHotkey(entry.Hotkey))
            {
                MessageBox.Show(
                    $"方案 \"{entry.ProfileName}\" 的快捷键无效，请重新设置。",
                    "快捷键无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            allHotkeys.Add(entry.Hotkey);
            profileHotkeys[entry.ProfileName] = entry.Hotkey;
        }

        // 检查所有快捷键（含方案快捷键）是否有重复
        var allDistinct = allHotkeys.Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var allCount = allHotkeys.Count(h => !string.IsNullOrWhiteSpace(h));
        if (allDistinct < allCount)
        {
            MessageBox.Show(
                "存在重复的快捷键，请修改冲突项。",
                "快捷键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new HotkeySettings
        {
            StartStopRecording = selected[0] ?? "",
            PauseRecording = selected[1] ?? "",
            EmergencyStop = selected[2] ?? "",
            StartPlayback = selected[3] ?? "",
            ProfileHotkeys = profileHotkeys
        };

        DialogResult = true;
        Close();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tbName }) return;
        var tb = FindName(tbName) as TextBox;
        if (tb is not null) tb.Text = "";
    }

    private void ClearProfileHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileHotkeyEntry entry })
        {
            entry.Hotkey = "";
            // 刷新 ItemsControl
            ProfileHotkeyList.ItemsSource = null;
            ProfileHotkeyList.ItemsSource = _profileEntries;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
