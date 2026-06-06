using System.Drawing;
using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

/// <summary>
/// 系统托盘图标管理：空闲显示应用图标，录制显示红色，播放显示蓝色。
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly Icon _playingIcon;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleRecordRequested;
    public event EventHandler? TogglePlaybackRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? MiniModeRequested;

    public TrayIconService()
    {
        _idleIcon = LoadAppIcon();
        _recordingIcon = CreateColorIcon(Color.FromArgb(244, 67, 54));   // 红色
        _playingIcon = CreateColorIcon(Color.FromArgb(33, 150, 243));    // 蓝色

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("迷你模式", null, (_, _) => MiniModeRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("录制", null, (_, _) => ToggleRecordRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("播放", null, (_, _) => TogglePlaybackRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("停止", null, (_, _) => StopRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _idleIcon,
            Text = "全能脚本V2.2.1",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetState(bool isRecording, bool isPlaying)
    {
        if (isRecording)
        {
            _notifyIcon.Icon = _recordingIcon;
            _notifyIcon.Text = "全能脚本V2.2.1 - 录制中";
        }
        else if (isPlaying)
        {
            _notifyIcon.Icon = _playingIcon;
            _notifyIcon.Text = "全能脚本V2.2.1 - 播放中";
        }
        else
        {
            _notifyIcon.Icon = _idleIcon;
            _notifyIcon.Text = "全能脚本V2.2.1";
        }
    }

    private static Icon LoadAppIcon()
    {
        // 从嵌入资源加载应用图标
        var uri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
        var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream is not null)
            return new Icon(stream, 16, 16);

        // fallback: 生成绿色图标
        return CreateColorIcon(Color.FromArgb(76, 175, 80));
    }

    /// <summary>
    /// 动态生成 16x16 纯色圆形图标
    /// </summary>
    private static Icon CreateColorIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _playingIcon.Dispose();
    }
}
