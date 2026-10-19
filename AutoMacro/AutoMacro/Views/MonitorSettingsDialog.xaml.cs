using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoMacro.Models;
using AutoMacro.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AutoMacro.Views;

public partial class MonitorSettingsDialog : FluentWindow
{
    private readonly IMonitorTriggerService _monitorTriggerService;
    private MonitorTriggerSettings _settings;
    private OcrRegion? _selectedRegion;
    private string _imagePath = string.Empty;

    public MonitorTriggerSettings? Result { get; private set; }

    public MonitorSettingsDialog(
        MonitorTriggerSettings settings,
        IMonitorTriggerService monitorTriggerService)
    {
        _settings = settings.Clone();
        _settings.Normalize();
        _monitorTriggerService = monitorTriggerService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        TriggerTypeBox.SelectedIndex = _settings.TriggerType switch
        {
            MonitorTriggerType.Text => 1,
            MonitorTriggerType.Window => 2,
            _ => 0
        };
        _imagePath = _settings.ImagePath;
        TargetTextBox.Text = _settings.TextPattern;
        WindowTitleBox.Text = _settings.WindowTitle;
        _selectedRegion = _settings.GetRegion();
        ScanIntervalBox.Value = _settings.ScanIntervalMs;
        ConsecutiveMatchesBox.Value = _settings.ConsecutiveMatches;
        ThresholdBox.Value = _settings.MatchThreshold;
        CooldownBox.Value = _settings.CooldownSeconds;
        UpdateImagePreview();
        UpdateRegionSummary();
        UpdateTargetPanels();
    }

    private MonitorTriggerType SelectedTriggerType => TriggerTypeBox.SelectedIndex switch
    {
        1 => MonitorTriggerType.Text,
        2 => MonitorTriggerType.Window,
        _ => MonitorTriggerType.Image
    };

    private void TriggerTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateTargetPanels();
        HideTestResult();
    }

    private void UpdateTargetPanels()
    {
        var type = SelectedTriggerType;
        ImageTargetPanel.Visibility = type == MonitorTriggerType.Image ? Visibility.Visible : Visibility.Collapsed;
        TextTargetPanel.Visibility = type == MonitorTriggerType.Text ? Visibility.Visible : Visibility.Collapsed;
        WindowTargetPanel.Visibility = type == MonitorTriggerType.Window ? Visibility.Visible : Visibility.Collapsed;
        RegionPanel.Visibility = type == MonitorTriggerType.Window ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要监控的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _imagePath = dialog.FileName;
        UpdateImagePreview();
        HideTestResult();
    }

    private async void CaptureTarget_Click(object sender, RoutedEventArgs e)
    {
        ScreenRegionSelectorWindow? selector = null;
        try
        {
            selector = new ScreenRegionSelectorWindow(captureSelectedImage: true)
            {
                Owner = this
            };
            var accepted = await RunWithAppWindowsTransparentAsync(
                () => Task.FromResult(selector.ShowDialog() == true));
            if (!accepted || selector.SelectedRegion is null)
                return;

            var region = selector.SelectedRegion;
            if (selector.CapturedImage is null)
            {
                ShowTestResult(
                    string.IsNullOrWhiteSpace(selector.CaptureError)
                        ? "没有截取到目标画面，请重试。"
                        : $"截图失败：{selector.CaptureError}",
                    success: false);
                return;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"AutoMacroMonitor_{Guid.NewGuid():N}.png");
            selector.CapturedImage.Save(tempPath, ImageFormat.Png);
            _imagePath = tempPath;
            _selectedRegion = region;
            UpdateImagePreview();
            UpdateRegionSummary();
            HideTestResult();
        }
        finally
        {
            selector?.CapturedImage?.Dispose();
        }
    }

    private async void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        var selector = new ScreenRegionSelectorWindow
        {
            Owner = this
        };
        var accepted = await RunWithAppWindowsTransparentAsync(
            () => Task.FromResult(selector.ShowDialog() == true));
        if (accepted)
            _selectedRegion = selector.SelectedRegion;

        UpdateRegionSummary();
        HideTestResult();
    }

    private void ClearRegion_Click(object sender, RoutedEventArgs e)
    {
        _selectedRegion = null;
        UpdateRegionSummary();
        HideTestResult();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        var validation = ValidateSettings(settings);
        if (validation is not null)
        {
            ShowTestResult(validation, success: false);
            return;
        }

        ShowTestResult("正在识别当前画面，请稍候……", success: true);
        TestButton.IsEnabled = false;
        try
        {
            var result = await RunWithAppWindowsTransparentAsync(() => Task.Run(() =>
                _monitorTriggerService.TestOnceAsync(
                    settings,
                    CancellationToken.None,
                    maskOwnWindows: false)));
            ShowTestResult(result.Message, result.Matched);
        }
        catch (Exception ex)
        {
            ShowTestResult($"测试失败：{ex.Message}", success: false);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        var validation = ValidateSettings(settings);
        if (validation is not null)
        {
            ShowTestResult(validation, success: false);
            return;
        }

        Result = settings;
        DialogResult = true;
        Close();
    }

    private MonitorTriggerSettings ReadSettings()
    {
        var settings = new MonitorTriggerSettings
        {
            TriggerType = SelectedTriggerType,
            ImagePath = _imagePath,
            TextPattern = TargetTextBox.Text.Trim(),
            WindowTitle = WindowTitleBox.Text.Trim(),
            MatchThreshold = ThresholdBox.Value ?? 0.92,
            ScanIntervalMs = (int)Math.Round(ScanIntervalBox.Value ?? 800),
            ConsecutiveMatches = (int)Math.Round(ConsecutiveMatchesBox.Value ?? 2),
            CooldownSeconds = (int)Math.Round(CooldownBox.Value ?? 10)
        };

        if (_selectedRegion is not null && settings.TriggerType != MonitorTriggerType.Window)
        {
            settings.UseRegion = true;
            settings.RegionX = _selectedRegion.X;
            settings.RegionY = _selectedRegion.Y;
            settings.RegionWidth = _selectedRegion.Width;
            settings.RegionHeight = _selectedRegion.Height;
        }

        settings.Normalize();
        return settings;
    }

    private static string? ValidateSettings(MonitorTriggerSettings settings)
    {
        return settings.TriggerType switch
        {
            MonitorTriggerType.Image when string.IsNullOrWhiteSpace(settings.ImagePath) =>
                "请先框选画面或选择一张目标图片。",
            MonitorTriggerType.Image when !File.Exists(settings.ImagePath) =>
                "目标图片不存在，请重新选择。",
            MonitorTriggerType.Text when string.IsNullOrWhiteSpace(settings.TextPattern) =>
                "请输入要监控的文字。",
            MonitorTriggerType.Window when string.IsNullOrWhiteSpace(settings.WindowTitle) =>
                "请输入要监控的窗口标题关键词。",
            _ => null
        };
    }

    private void UpdateImagePreview()
    {
        ImagePathText.Text = string.IsNullOrWhiteSpace(_imagePath)
            ? "尚未选择"
            : _imagePath;
        PreviewPlaceholder.Visibility = File.Exists(_imagePath) ? Visibility.Collapsed : Visibility.Visible;
        TargetPreview.Source = File.Exists(_imagePath) ? LoadBitmap(_imagePath) : null;
    }

    private void UpdateRegionSummary()
    {
        RegionSummaryText.Text = _selectedRegion is null
            ? "整个屏幕"
            : $"区域 {_selectedRegion.Width}×{_selectedRegion.Height}，位置 ({_selectedRegion.X}, {_selectedRegion.Y})";
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ShowTestResult(string message, bool success)
    {
        TestResultBorder.Visibility = Visibility.Visible;
        TestResultBorder.Background = new SolidColorBrush(
            success ? System.Windows.Media.Color.FromRgb(21, 52, 47) : System.Windows.Media.Color.FromRgb(64, 31, 31));
        TestResultBorder.BorderBrush = new SolidColorBrush(
            success ? System.Windows.Media.Color.FromRgb(49, 132, 104) : System.Windows.Media.Color.FromRgb(148, 66, 66));
        TestResultText.Text = message;
    }

    private void HideTestResult()
    {
        TestResultBorder.Visibility = Visibility.Collapsed;
    }

    private async Task<T> RunWithAppWindowsTransparentAsync<T>(Func<Task<T>> operation)
    {
        var ownerWindow = Owner;
        var dialogOpacity = Opacity;
        var ownerOpacity = ownerWindow?.Opacity ?? 1;

        try
        {
            Opacity = 0;
            if (ownerWindow is not null)
                ownerWindow.Opacity = 0;

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(150);
            return await operation();
        }
        finally
        {
            if (ownerWindow is not null)
                ownerWindow.Opacity = ownerOpacity;
            Opacity = dialogOpacity;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            Activate();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
