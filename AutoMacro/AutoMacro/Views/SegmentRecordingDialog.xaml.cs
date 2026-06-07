using System.ComponentModel;
using Wpf.Ui.Controls;

namespace AutoMacro.Views;

public partial class SegmentRecordingDialog : FluentWindow
{
    private bool _isRecording;
    private bool _accepted;

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;

    public SegmentRecordingDialog()
    {
        InitializeComponent();
    }

    public void TriggerHotkey()
    {
        if (_isRecording)
            StopAndAccept();
        else
            StartSegmentRecording();
    }

    private void Start_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartSegmentRecording();
    }

    private void Stop_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StopAndAccept();
    }

    private void StartSegmentRecording()
    {
        if (_isRecording)
            return;

        StartRequested?.Invoke(this, EventArgs.Empty);
        _isRecording = true;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "正在录制鼠标和键盘。完成后按 F10，或回到这里点“停止并插入”。";
    }

    private void StopAndAccept()
    {
        if (!_isRecording)
            return;

        StopRequested?.Invoke(this, EventArgs.Empty);
        _isRecording = false;
        _accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_accepted || !_isRecording)
            return;

        StopRequested?.Invoke(this, EventArgs.Empty);
        _isRecording = false;
    }
}
