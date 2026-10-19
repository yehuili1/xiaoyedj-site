using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AutoMacro.Services;
using WinForms = System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPoint = System.Drawing.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace AutoMacro.Views;

public partial class ScreenRegionSelectorWindow : Window
{
    private WpfPoint? _start;
    private DrawingPoint? _startScreen;
    private readonly bool _captureSelectedImage;
    private bool _finishing;

    public OcrRegion? SelectedRegion { get; private set; }
    public DrawingBitmap? CapturedImage { get; private set; }
    public string CaptureError { get; private set; } = string.Empty;

    public ScreenRegionSelectorWindow(bool captureSelectedImage = false)
    {
        _captureSelectedImage = captureSelectedImage;
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
        _startScreen = WinForms.Cursor.Position;
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

    private async void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null || _startScreen is null || _finishing || e.ChangedButton != MouseButton.Left)
            return;

        _finishing = true;
        var end = e.GetPosition(this);
        var endScreen = WinForms.Cursor.Position;
        ReleaseMouseCapture();

        var left = Math.Min(_startScreen.Value.X, endScreen.X);
        var top = Math.Min(_startScreen.Value.Y, endScreen.Y);
        var width = Math.Abs(endScreen.X - _startScreen.Value.X);
        var height = Math.Abs(endScreen.Y - _startScreen.Value.Y);

        if (width < 8 || height < 8)
        {
            DialogResult = false;
            Close();
            return;
        }

        SelectedRegion = new OcrRegion(left, top, width, height);

        if (_captureSelectedImage)
        {
            try
            {
                // 先让框选遮罩从桌面合成画面中消失，再截取真实内容。
                Opacity = 0;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(150);

                var bitmap = new DrawingBitmap(width, height, PixelFormat.Format24bppRgb);
                try
                {
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
                    CapturedImage = bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                CaptureError = ex.Message;
            }
        }

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
