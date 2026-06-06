using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMacro.Services;
using WinForms = System.Windows.Forms;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace AutoMacro.Views;

public partial class ScreenRegionSelectorWindow : Window
{
    private WpfPoint? _start;

    public OcrRegion? SelectedRegion { get; private set; }

    public ScreenRegionSelectorWindow()
    {
        InitializeComponent();

        var screen = WinForms.SystemInformation.VirtualScreen;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _start = e.GetPosition(this);
        CaptureMouse();
        UpdateSelection(_start.Value, _start.Value);
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_start is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        UpdateSelection(_start.Value, e.GetPosition(this));
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null || e.ChangedButton != MouseButton.Left)
            return;

        var end = e.GetPosition(this);
        ReleaseMouseCapture();

        var left = Math.Min(_start.Value.X, end.X);
        var top = Math.Min(_start.Value.Y, end.Y);
        var width = Math.Abs(end.X - _start.Value.X);
        var height = Math.Abs(end.Y - _start.Value.Y);

        if (width < 8 || height < 8)
        {
            DialogResult = false;
            Close();
            return;
        }

        SelectedRegion = new OcrRegion(
            (int)Math.Round(Left + left),
            (int)Math.Round(Top + top),
            (int)Math.Round(width),
            (int)Math.Round(height));
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        Close();
    }

    private void UpdateSelection(WpfPoint start, WpfPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
    }
}
