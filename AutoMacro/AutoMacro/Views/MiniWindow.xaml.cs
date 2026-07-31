using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoMacro.Models;
using AutoMacro.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AutoMacro.Views;

public partial class MiniWindow : Window
{
    private const double SnapDistance = 24;
    private const double VisibleTabSize = 8;
    private const int AnimationMilliseconds = 160;
    private const int AutoHideDelayMilliseconds = 450;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _autoHideTimer;
    private DockEdge _dockEdge = DockEdge.None;
    private bool _isDocked;
    private bool _isHidden;
    private bool _isDragging;

    public event EventHandler? RestoreRequested;

    public MiniWindow(MainViewModel viewModel, AppSettings settings)
    {
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoHideDelayMilliseconds)
        };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (_isDocked && !_isDragging && !IsMouseOver)
                HideToEdge(animated: true);
        };

        MouseEnter += (_, _) => ShowFromEdge(animated: true);
        MouseLeave += (_, _) => ScheduleAutoHide();

        RestoreSavedPosition();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoHideTimer.Stop();
        SavePosition();
        base.OnClosed(e);
    }

    private void RestoreSavedPosition()
    {
        if (_settings.MiniWindowLeft is { } left &&
            _settings.MiniWindowTop is { } top &&
            IsFinite(left) &&
            IsFinite(top) &&
            IsMostlyVisible(left, top))
        {
            Left = left;
            Top = top;
            _dockEdge = ParseDockEdge(_settings.MiniWindowDockEdge);
            _isDocked = _settings.MiniWindowIsDocked && _dockEdge != DockEdge.None;
            if (_isDocked)
            {
                SnapToEdge(_dockEdge, GetCurrentWorkArea(), animated: false);
                HideToEdge(animated: false);
            }
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
        _dockEdge = DockEdge.None;
        _isDocked = false;
        _isHidden = false;
    }

    private void SavePosition()
    {
        if (!IsFinite(Left) || !IsFinite(Top))
            return;

        var (left, top) = GetPositionToSave();
        _settings.MiniWindowLeft = left;
        _settings.MiniWindowTop = top;
        _settings.MiniWindowIsDocked = _isDocked;
        _settings.MiniWindowDockEdge = _isDocked ? _dockEdge.ToString() : null;
        try
        {
            _settings.Save();
        }
        catch
        {
            // 位置记忆失败不影响脚本执行。
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        ShowFromEdge(animated: false);

        try
        {
            _isDragging = true;
            DragMove();
        }
        finally
        {
            _isDragging = false;
        }

        ApplyDockingAfterDrag();
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private bool IsMostlyVisible(double left, double top)
    {
        const double minVisibleSize = 40;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        return left + Width >= virtualLeft + minVisibleSize &&
               left <= virtualRight - minVisibleSize &&
               top + Height >= virtualTop + minVisibleSize &&
               top <= virtualBottom - minVisibleSize;
    }

    private void ApplyDockingAfterDrag()
    {
        var workArea = GetCurrentWorkArea();
        var edge = FindNearestDockEdge(workArea);
        if (edge == DockEdge.None)
        {
            _isDocked = false;
            _isHidden = false;
            _dockEdge = DockEdge.None;
            SavePosition();
            return;
        }

        _isDocked = true;
        _isHidden = false;
        _dockEdge = edge;
        SnapToEdge(edge, workArea, animated: true);
        SavePosition();
        ScheduleAutoHide(TimeSpan.FromMilliseconds(250));
    }

    private void ShowFromEdge(bool animated)
    {
        if (!_isDocked || !_isHidden || _dockEdge == DockEdge.None)
            return;

        _autoHideTimer.Stop();
        var workArea = GetCurrentWorkArea();
        var (left, top) = GetShownPosition(_dockEdge, workArea);
        _isHidden = false;
        MoveTo(left, top, animated);
    }

    private void HideToEdge(bool animated)
    {
        if (!_isDocked || _dockEdge == DockEdge.None)
            return;

        var workArea = GetCurrentWorkArea();
        var (left, top) = GetHiddenPosition(_dockEdge, workArea);
        _isHidden = true;
        MoveTo(left, top, animated);
    }

    private void ScheduleAutoHide()
    {
        ScheduleAutoHide(TimeSpan.FromMilliseconds(AutoHideDelayMilliseconds));
    }

    private void ScheduleAutoHide(TimeSpan delay)
    {
        if (!_isDocked || _isDragging)
            return;

        _autoHideTimer.Stop();
        _autoHideTimer.Interval = delay;
        _autoHideTimer.Start();
    }

    private void SnapToEdge(DockEdge edge, Rect workArea, bool animated)
    {
        var (left, top) = GetShownPosition(edge, workArea);
        MoveTo(left, top, animated);
    }

    private DockEdge FindNearestDockEdge(Rect workArea)
    {
        var distances = new[]
        {
            (Edge: DockEdge.Left, Distance: Math.Abs(Left - workArea.Left)),
            (Edge: DockEdge.Right, Distance: Math.Abs(workArea.Right - (Left + Width))),
            (Edge: DockEdge.Top, Distance: Math.Abs(Top - workArea.Top)),
            (Edge: DockEdge.Bottom, Distance: Math.Abs(workArea.Bottom - (Top + Height)))
        };

        var nearest = distances.OrderBy(item => item.Distance).First();
        return nearest.Distance <= SnapDistance ? nearest.Edge : DockEdge.None;
    }

    private (double Left, double Top) GetShownPosition(DockEdge edge, Rect workArea)
    {
        var left = Left;
        var top = Top;

        switch (edge)
        {
            case DockEdge.Left:
                left = workArea.Left;
                top = Clamp(top, workArea.Top, workArea.Bottom - Height);
                break;
            case DockEdge.Right:
                left = workArea.Right - Width;
                top = Clamp(top, workArea.Top, workArea.Bottom - Height);
                break;
            case DockEdge.Top:
                left = Clamp(left, workArea.Left, workArea.Right - Width);
                top = workArea.Top;
                break;
            case DockEdge.Bottom:
                left = Clamp(left, workArea.Left, workArea.Right - Width);
                top = workArea.Bottom - Height;
                break;
        }

        return (left, top);
    }

    private (double Left, double Top) GetHiddenPosition(DockEdge edge, Rect workArea)
    {
        var (left, top) = GetShownPosition(edge, workArea);

        return edge switch
        {
            DockEdge.Left => (workArea.Left - Width + VisibleTabSize, top),
            DockEdge.Right => (workArea.Right - VisibleTabSize, top),
            DockEdge.Top => (left, workArea.Top - Height + VisibleTabSize),
            DockEdge.Bottom => (left, workArea.Bottom - VisibleTabSize),
            _ => (left, top)
        };
    }

    private (double Left, double Top) GetPositionToSave()
    {
        if (!_isDocked || _dockEdge == DockEdge.None)
            return (Left, Top);

        return GetShownPosition(_dockEdge, GetCurrentWorkArea());
    }

    private Rect GetCurrentWorkArea()
    {
        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var bounds = new Drawing.Rectangle(
            (int)Math.Round(Left * scaleX),
            (int)Math.Round(Top * scaleY),
            Math.Max(1, (int)Math.Round(Width * scaleX)),
            Math.Max(1, (int)Math.Round(Height * scaleY)));

        var workArea = Forms.Screen.FromRectangle(bounds).WorkingArea;
        return new Rect(
            workArea.Left / scaleX,
            workArea.Top / scaleY,
            workArea.Width / scaleX,
            workArea.Height / scaleY);
    }

    private void MoveTo(double left, double top, bool animated)
    {
        if (!animated)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = left;
            Top = top;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(AnimationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var leftAnimation = new DoubleAnimation(Left, left, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var topAnimation = new DoubleAnimation(Top, top, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        leftAnimation.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = left;
            Top = top;
        };

        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private static DockEdge ParseDockEdge(string? value)
    {
        return Enum.TryParse<DockEdge>(value, out var edge) ? edge : DockEdge.None;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
            return min;

        return Math.Clamp(value, min, max);
    }

    private enum DockEdge
    {
        None,
        Left,
        Top,
        Right,
        Bottom
    }
}
