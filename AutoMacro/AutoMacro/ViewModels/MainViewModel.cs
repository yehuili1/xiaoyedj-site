using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using AutoMacro.Models;
using AutoMacro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace AutoMacro.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const double MinPlaybackSpeed = 0.1;
    private const double MaxPlaybackSpeed = 10.0;

    private readonly IProfileManager _profileManager;
    private readonly IRecordingService _recordingService;
    private readonly IPlaybackService _playbackService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IRunLogger _logger;

    public ActionListViewModel ActionListVm { get; }
    public VariableEditorViewModel VariableEditorVm { get; }

    [ObservableProperty]
    private ObservableCollection<RecordProfile> _profiles = new();

    [ObservableProperty]
    private RecordProfile? _selectedProfile;

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _treeNodes = new();

    [ObservableProperty]
    private TreeNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    private int _activeTabIndex;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _loopCount = 1;

    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    // 快捷键显示文本
    [ObservableProperty]
    private string _hotkeyRecordText = "";

    [ObservableProperty]
    private string _hotkeyPauseText = "";

    [ObservableProperty]
    private string _hotkeyStopText = "";

    [ObservableProperty]
    private string _hotkeyPlaybackText = "";

    public string RecordButtonText => IsRecording ? "停止录制" : "开始录制";
    public string PauseButtonText => IsPaused ? "继续" : "暂停";
    public string PlaybackButtonText => IsPlaying ? "停止回放" : "开始回放";
    public bool ShowPauseButton => IsRecording || IsPlaying;

    public MainViewModel(
        IProfileManager profileManager,
        IRecordingService recordingService,
        IPlaybackService playbackService,
        IHotkeyService hotkeyService,
        IRunLogger logger,
        ActionListViewModel actionListVm,
        VariableEditorViewModel variableEditorVm)
    {
        _profileManager = profileManager;
        _recordingService = recordingService;
        _playbackService = playbackService;
        _hotkeyService = hotkeyService;
        _logger = logger;
        ActionListVm = actionListVm;
        VariableEditorVm = variableEditorVm;

        _profileManager.LoadAllProfiles();
        Profiles = _profileManager.Profiles;

        _recordingService.EventRecorded += OnEventRecorded;
        _playbackService.PlaybackStopped += OnPlaybackStopped;
        _playbackService.LoopChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(UpdateStatus);

        // 绑定热键事件（用 BeginInvoke 异步派发，避免阻塞钩子线程导致卡顿）
        _hotkeyService.RecordHotkeyPressed += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() => ToggleRecording());
        _hotkeyService.PauseRecordHotkeyPressed += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() => TogglePause());
        _hotkeyService.StopHotkeyPressed += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(EmergencyStop);
        _hotkeyService.PlaybackHotkeyPressed += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(HandlePlaybackHotkey);
        _hotkeyService.ProfilePlaybackRequested += (_, profileName) =>
            Application.Current.Dispatcher.BeginInvoke(() => HandleProfilePlaybackHotkey(profileName));
        _hotkeyService.StartListening();

        RefreshHotkeyTexts();
        _logger.Info("MainViewModel", "主界面模型初始化完成");
    }

    private void RefreshHotkeyTexts()
    {
        var s = _hotkeyService.Settings;
        HotkeyRecordText = s.StartStopRecording;
        HotkeyPauseText = s.PauseRecording;
        HotkeyStopText = s.EmergencyStop;
        HotkeyPlaybackText = s.StartPlayback;
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(ShowPauseButton));
        UpdateStatus();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
        UpdateStatus();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPauseButton));
        OnPropertyChanged(nameof(PlaybackButtonText));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsRecording && IsPaused)
            StatusText = "录制已暂停";
        else if (IsRecording)
            StatusText = "录制中...";
        else if (IsPlaying)
            StatusText = $"回放中 (第{_playbackService.CurrentLoop}轮, {PlaybackSpeed:0.##}x)...";
        else
            StatusText = "就绪";
    }

    partial void OnLoopCountChanged(int value)
    {
        if (SelectedProfile is null) return;

        var normalized = Math.Max(0, value);
        if (SelectedProfile.LoopCount == normalized) return;

        SelectedProfile.LoopCount = normalized;
        _profileManager.SaveProfile(SelectedProfile);
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        var normalized = NormalizePlaybackSpeed(value);
        if (Math.Abs(value - normalized) > 0.001)
        {
            PlaybackSpeed = normalized;
            return;
        }

        if (SelectedProfile is null) return;
        if (Math.Abs(SelectedProfile.PlaybackSpeed - normalized) < 0.001) return;

        SelectedProfile.PlaybackSpeed = normalized;
        _profileManager.SaveProfile(SelectedProfile);
        _playbackService.SetPlaybackSpeed(normalized);
        UpdateStatus();
    }

    partial void OnSelectedProfileChanged(RecordProfile? value)
    {
        if (value is null)
        {
            if (LoopCount != 1)
                LoopCount = 1;
            if (Math.Abs(PlaybackSpeed - 1.0) > 0.001)
                PlaybackSpeed = 1.0;
            TreeNodes.Clear();
            return;
        }

        if (LoopCount != value.LoopCount)
            LoopCount = value.LoopCount;

        var speed = NormalizePlaybackSpeed(value.PlaybackSpeed);
        if (Math.Abs(PlaybackSpeed - speed) > 0.001)
            PlaybackSpeed = speed;

        ActionListVm.LoadFromProfile(value);
        VariableEditorVm.LoadFromProfile(value);

        var root = new TreeNodeViewModel
        {
            Name = value.Name,
            IconGlyph = SymbolRegular.Folder24,
            Tag = "Root",
            IsExpanded = true,
            Children = new ObservableCollection<TreeNodeViewModel>
            {
                new() { Name = "动作脚本", IconGlyph = SymbolRegular.Script24, Tag = "ActionScript" },
                new() { Name = "变量表", IconGlyph = SymbolRegular.Table24, Tag = "VariableTable" },
            }
        };
        TreeNodes = new ObservableCollection<TreeNodeViewModel> { root };
        SelectedTreeNode = root.Children[0];
    }

    partial void OnSelectedTreeNodeChanged(TreeNodeViewModel? value)
    {
        if (value is null) return;
        ActiveTabIndex = value.Tag switch
        {
            "ActionScript" => 0,
            "VariableTable" => 1,
            _ => ActiveTabIndex
        };
    }

    [RelayCommand]
    private void NewProfile()
    {
        var dialog = new Views.NewProfileDialog
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        var name = dialog.ProfileName;
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var profile = _profileManager.CreateProfile(name);
            Profiles = _profileManager.Profiles;
            SelectedProfile = profile;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "新建方案失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile is null) return;

        var dialog = new Views.NewProfileDialog(SelectedProfile.Name)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        var newName = dialog.ProfileName;
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedProfile.Name) return;

        try
        {
            var renamed = _profileManager.RenameProfile(SelectedProfile, newName);
            Profiles = _profileManager.Profiles;
            SelectedProfile = renamed;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "重命名失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;

        var result = System.Windows.MessageBox.Show(
            $"确定要删除方案 \"{SelectedProfile.Name}\" 吗？\n此操作不可撤销。",
            "删除方案",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var profile = SelectedProfile;
        SelectedProfile = null;
        _profileManager.DeleteProfile(profile);
        Profiles = _profileManager.Profiles;
    }

    [RelayCommand]
    private void OpenHotkeySettings()
    {
        var profileNames = Profiles.Select(p => p.Name);
        var dialog = new Views.HotkeySettingsDialog(_hotkeyService.Settings, profileNames)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        dialog.Result.Save();
        _hotkeyService.ReloadSettings();
        RefreshHotkeyTexts();

        System.Windows.MessageBox.Show(
            "快捷键已更新。",
            "设置成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ExportProfile()
    {
        if (SelectedProfile is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出方案",
            FileName = $"{SelectedProfile.Name}.zip",
            Filter = "ZIP 压缩包|*.zip",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _profileManager.ExportProfile(SelectedProfile, dialog.FileName);
            System.Windows.MessageBox.Show(
                $"方案 \"{SelectedProfile.Name}\" 已导出到:\n{dialog.FileName}",
                "导出成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "导出失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入方案",
            Filter = "ZIP 压缩包|*.zip",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var profile = _profileManager.ImportProfile(dialog.FileName);
            Profiles = _profileManager.Profiles;
            SelectedProfile = profile;
            System.Windows.MessageBox.Show(
                $"方案 \"{profile.Name}\" 已导入成功。",
                "导入成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "导入失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (IsPlaying) return;

        if (IsRecording)
        {
            _recordingService.StopRecording();
            IsRecording = false;
            IsPaused = false;

            if (SelectedProfile is not null)
            {
                var events = _recordingService.GetRecordedEvents();
                _profileManager.SaveActions(SelectedProfile, events);
                VariableEditorVm.SaveCsv();
                ActionListVm.LoadFromProfile(SelectedProfile);
            }
        }
        else
        {
            if (SelectedProfile is null)
            {
                NewProfile();
                if (SelectedProfile is null) return;
            }
            _recordingService.StartRecording();
            IsRecording = true;
            IsPaused = false;
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        // 录制中：暂停/继续录制
        if (IsRecording)
        {
            if (IsPaused)
            {
                _recordingService.ResumeRecording();
                IsPaused = false;
            }
            else
            {
                _recordingService.PauseRecording();
                IsPaused = true;
            }
            return;
        }

        // 回放中：暂停/继续回放
        if (IsPlaying)
        {
            if (_playbackService.IsPaused)
            {
                _playbackService.ResumePlayback();
                IsPaused = false;
            }
            else
            {
                _playbackService.PausePlayback();
                IsPaused = true;
            }
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_logger.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogDirectory,
                UseShellExecute = true
            });
            _logger.Info("Logs", $"打开日志目录: {_logger.LogDirectory}");
        }
        catch (Exception ex)
        {
            _logger.Error("Logs", "打开日志目录失败", ex);
            System.Windows.MessageBox.Show(ex.Message, "打开日志失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task TogglePlayback()
    {
        if (IsRecording) return;

        if (IsPlaying)
        {
            _playbackService.StopPlayback();
            return;
        }

        if (SelectedProfile is null || ActionListVm.Actions.Count == 0)
            return;

        VariableEditorVm.SaveCsv();
        IsPlaying = true;
        await _playbackService.StartPlaybackAsync(
            ActionListVm.Actions.ToList(),
            VariableEditorVm.VariableTable,
            SelectedProfile.LoopCount,
            PlaybackSpeed);
    }

    /// <summary>
    /// 按方案名直接启动回放（由方案快捷键触发）
    /// </summary>
    private async Task PlayProfileByName(string profileName)
    {
        if (IsRecording || IsPlaying) return;

        var profile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return;

        // 切换到目标方案并加载数据
        SelectedProfile = profile;

        if (ActionListVm.Actions.Count == 0) return;

        VariableEditorVm.SaveCsv();
        IsPlaying = true;
        await _playbackService.StartPlaybackAsync(
            ActionListVm.Actions.ToList(),
            VariableEditorVm.VariableTable,
            profile.LoopCount,
            PlaybackSpeed);
    }

    private async void HandlePlaybackHotkey()
    {
        try
        {
            await TogglePlayback();
        }
        catch
        {
            IsPlaying = false;
            IsPaused = false;
        }
    }

    private async void HandleProfilePlaybackHotkey(string profileName)
    {
        try
        {
            await PlayProfileByName(profileName);
        }
        catch
        {
            IsPlaying = false;
            IsPaused = false;
        }
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        if (IsRecording)
        {
            _recordingService.StopRecording();
            IsRecording = false;
            IsPaused = false;
        }
        if (IsPlaying)
        {
            _playbackService.StopPlayback();
        }
    }

    private void OnEventRecorded(object? sender, InputEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ActionListVm.Actions.Add(e);
        });
    }

    private void OnPlaybackStopped(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsPlaying = false;
            IsPaused = false;
        });
    }

    private static double NormalizePlaybackSpeed(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 1.0;

        return Math.Clamp(value, MinPlaybackSpeed, MaxPlaybackSpeed);
    }
}
