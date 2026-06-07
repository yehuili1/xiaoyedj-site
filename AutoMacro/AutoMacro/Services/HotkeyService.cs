using AutoMacro.Models;
using SharpHook;
using SharpHook.Data;

namespace AutoMacro.Services;

public class HotkeyService : IHotkeyService
{
    private readonly IGlobalHookProvider _hookProvider;
    private readonly IRunLogger _logger;
    private bool _listening;
    private HashSet<KeyCode> _suppressedKeys = new();

    public HotkeySettings Settings { get; private set; }

    private HotkeyBinding _recordBinding;
    private HotkeyBinding _pauseBinding;
    private HotkeyBinding _stopBinding;
    private HotkeyBinding _playbackBinding;
    private HotkeyBinding _insertRecordingBinding;

    public event EventHandler? RecordHotkeyPressed;
    public event EventHandler? PauseRecordHotkeyPressed;
    public event EventHandler? StopHotkeyPressed;
    public event EventHandler? PlaybackHotkeyPressed;
    public event EventHandler? InsertRecordingHotkeyPressed;
    public event EventHandler<string>? ProfilePlaybackRequested;

    private Dictionary<string, HotkeyBinding> _profileBindings = new();

    public HotkeyService(IGlobalHookProvider hookProvider, IRunLogger logger)
    {
        _hookProvider = hookProvider;
        _logger = logger;
        Settings = HotkeySettings.Load();
        ApplySettings();
    }

    public void ReloadSettings()
    {
        Settings = HotkeySettings.Load();
        ApplySettings();
        _logger.Info("Hotkey", "快捷键配置已重新加载");
    }

    private void ApplySettings()
    {
        _recordBinding = HotkeySettings.ParseHotkey(Settings.StartStopRecording);
        _pauseBinding = HotkeySettings.ParseHotkey(Settings.PauseRecording);
        _stopBinding = HotkeySettings.ParseHotkey(Settings.EmergencyStop);
        _playbackBinding = HotkeySettings.ParseHotkey(Settings.StartPlayback);
        _insertRecordingBinding = HotkeySettings.ParseHotkey(Settings.InsertRecording);
        if (string.Equals(Settings.InsertRecording, "F5", StringComparison.OrdinalIgnoreCase))
            _insertRecordingBinding = HotkeyBinding.Empty;

        _suppressedKeys = new HashSet<KeyCode>();
        AddSuppressedKey(_recordBinding);
        AddSuppressedKey(_pauseBinding);
        AddSuppressedKey(_stopBinding);
        AddSuppressedKey(_playbackBinding);
        AddSuppressedKey(_insertRecordingBinding);

        // 解析方案快捷键
        _profileBindings.Clear();
        foreach (var (profileName, hotkeyStr) in Settings.ProfileHotkeys)
        {
            var binding = HotkeySettings.ParseHotkey(hotkeyStr);
            if (binding.IsValid)
            {
                _profileBindings[profileName] = binding;
                _suppressedKeys.Add(binding.Key);
            }
        }

        _logger.Info("Hotkey",
            $"配置: record={Settings.StartStopRecording}, pause={Settings.PauseRecording}, stop={Settings.EmergencyStop}, playback={Settings.StartPlayback}, insertRecording={Settings.InsertRecording}, profileHotkeys={Settings.ProfileHotkeys.Count}");
    }

    private void AddSuppressedKey(HotkeyBinding binding)
    {
        if (binding.IsValid)
            _suppressedKeys.Add(binding.Key);
    }

    /// <summary>
    /// 返回所有被热键占用的主键 KeyCode，供录制服务过滤
    /// </summary>
    public HashSet<KeyCode> GetSuppressedKeys() => _suppressedKeys;

    public bool ShouldSuppress(KeyCode key, EventMask mask)
    {
        if (Matches(_recordBinding, key, mask)) return true;
        if (Matches(_pauseBinding, key, mask)) return true;
        if (Matches(_stopBinding, key, mask)) return true;
        if (Matches(_playbackBinding, key, mask)) return true;
        if (Matches(_insertRecordingBinding, key, mask)) return true;

        return _profileBindings.Values.Any(binding => Matches(binding, key, mask));
    }

    public void StartListening()
    {
        if (_listening) return;
        _listening = true;
        _hookProvider.Hook.KeyPressed += OnKeyPressed;
        _logger.Info("Hotkey", "开始监听全局快捷键");
        _ = _hookProvider.RunAsync().ContinueWith(task =>
        {
            if (task.Exception is not null)
                _logger.Error("Hotkey", "全局快捷键监听异常", task.Exception, captureScreenshot: true);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void StopListening()
    {
        if (!_listening) return;
        _listening = false;
        _hookProvider.Hook.KeyPressed -= OnKeyPressed;
        _logger.Info("Hotkey", "停止监听全局快捷键");
    }

    private static bool Matches(HotkeyBinding binding, KeyCode key, EventMask mask)
    {
        if (!binding.IsValid || key == KeyCode.VcUndefined || HotkeyBinding.IsModifierKey(key))
            return false;

        if (binding.Key != key) return false;

        // 使用 SharpHook 扩展方法，正确检测左/右修饰键
        bool ctrl = mask.HasCtrl();
        bool shift = mask.HasShift();
        bool alt = mask.HasAlt();

        return binding.Ctrl == ctrl && binding.Shift == shift && binding.Alt == alt;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;
        var mask = e.RawEvent.Mask;

        if (Matches(_recordBinding, key, mask))
        {
            _logger.Info("Hotkey", $"触发录制快捷键: {Settings.StartStopRecording}");
            RecordHotkeyPressed?.Invoke(this, EventArgs.Empty);
            e.SuppressEvent = true;
        }
        else if (Matches(_pauseBinding, key, mask))
        {
            _logger.Info("Hotkey", $"触发暂停快捷键: {Settings.PauseRecording}");
            PauseRecordHotkeyPressed?.Invoke(this, EventArgs.Empty);
            e.SuppressEvent = true;
        }
        else if (Matches(_stopBinding, key, mask))
        {
            _logger.Info("Hotkey", $"触发停止快捷键: {Settings.EmergencyStop}");
            StopHotkeyPressed?.Invoke(this, EventArgs.Empty);
            e.SuppressEvent = true;
        }
        else if (Matches(_playbackBinding, key, mask))
        {
            _logger.Info("Hotkey", $"触发回放快捷键: {Settings.StartPlayback}");
            PlaybackHotkeyPressed?.Invoke(this, EventArgs.Empty);
            e.SuppressEvent = true;
        }
        else if (Matches(_insertRecordingBinding, key, mask))
        {
            _logger.Info("Hotkey", $"触发插入录制快捷键: {Settings.InsertRecording}");
            InsertRecordingHotkeyPressed?.Invoke(this, EventArgs.Empty);
            e.SuppressEvent = true;
        }
        else
        {
            // 检查方案专属快捷键
            foreach (var (profileName, binding) in _profileBindings)
            {
                if (Matches(binding, key, mask))
                {
                    _logger.Info("Hotkey", $"触发方案快捷键: {profileName}");
                    ProfilePlaybackRequested?.Invoke(this, profileName);
                    e.SuppressEvent = true;
                    break;
                }
            }
        }
    }
}
