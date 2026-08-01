using System.Collections.ObjectModel;
using System.Windows;
using AutoMacro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace AutoMacro.Views;

public sealed record ScheduleModeOption(ScheduleRepeatMode Value, string Label);

public partial class ScheduleTaskEditorRow : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private ScheduleRepeatMode _repeatMode = ScheduleRepeatMode.IntervalHours;

    [ObservableProperty]
    private DateTime _runDate = DateTime.Today;

    [ObservableProperty]
    private string _timeText = "09:00";

    [ObservableProperty]
    private int _intervalHours = 1;

    [ObservableProperty]
    private int _intervalSeconds = 10;

    [ObservableProperty]
    private int _activeDurationMinutes;

    [ObservableProperty]
    private DateTime _intervalStartedAt = DateTime.Now;

    [ObservableProperty]
    private DateTime? _lastTriggeredAt;

    public bool IsOnce => RepeatMode == ScheduleRepeatMode.Once;
    public bool IsDaily => RepeatMode == ScheduleRepeatMode.Daily;
    public bool IsIntervalHours => RepeatMode == ScheduleRepeatMode.IntervalHours;
    public bool IsIntervalSeconds => RepeatMode == ScheduleRepeatMode.IntervalSeconds;
    public bool IsIntervalMode => IsIntervalHours || IsIntervalSeconds;

    public string TriggerHint => RepeatMode switch
    {
        ScheduleRepeatMode.IntervalHours => "从保存设置后开始计时，执行完会重新计时",
        ScheduleRepeatMode.IntervalSeconds => "从保存设置后开始计时，按秒重复执行",
        ScheduleRepeatMode.Daily => "每天固定时间执行一次",
        ScheduleRepeatMode.Once => "只在指定日期时间执行一次",
        _ => string.Empty
    };

    public string NextRunText => BuildRunText(ToTask().GetNextRunAfter(DateTime.Now));
    public string LastRunText => LastTriggeredAt is null ? "从未执行" : BuildRunText(LastTriggeredAt);

    partial void OnRepeatModeChanged(ScheduleRepeatMode value)
    {
        OnPropertyChanged(nameof(IsOnce));
        OnPropertyChanged(nameof(IsDaily));
        OnPropertyChanged(nameof(IsIntervalHours));
        OnPropertyChanged(nameof(IsIntervalSeconds));
        OnPropertyChanged(nameof(IsIntervalMode));
        OnPropertyChanged(nameof(TriggerHint));
        OnPropertyChanged(nameof(NextRunText));

        if (value is ScheduleRepeatMode.IntervalHours or ScheduleRepeatMode.IntervalSeconds)
            IntervalStartedAt = DateTime.Now;
    }

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(NextRunText));
    partial void OnRunDateChanged(DateTime value) => OnPropertyChanged(nameof(NextRunText));
    partial void OnTimeTextChanged(string value) => OnPropertyChanged(nameof(NextRunText));
    partial void OnIntervalHoursChanged(int value) => OnPropertyChanged(nameof(NextRunText));
    partial void OnIntervalSecondsChanged(int value) => OnPropertyChanged(nameof(NextRunText));
    partial void OnIntervalStartedAtChanged(DateTime value) => OnPropertyChanged(nameof(NextRunText));

    partial void OnLastTriggeredAtChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(NextRunText));
        OnPropertyChanged(nameof(LastRunText));
    }

    private ScheduledTaskItem ToTask() => new()
    {
        IsEnabled = IsEnabled,
        ProfileName = ProfileName,
        RepeatMode = RepeatMode,
        RunDate = RunDate == default ? DateTime.Today : RunDate.Date,
        TimeText = TimeText,
        IntervalHours = Math.Clamp(IntervalHours, 1, 720),
        IntervalSeconds = Math.Clamp(IntervalSeconds, 1, 86400),
        ActiveDurationMinutes = Math.Clamp(ActiveDurationMinutes, 0, 525600),
        IntervalStartedAt = IntervalStartedAt == default ? DateTime.Now : IntervalStartedAt,
        LastTriggeredAt = LastTriggeredAt
    };

    private static string BuildRunText(DateTime? value)
    {
        if (value is null)
            return "无";

        var runAt = value.Value;
        var today = DateTime.Today;
        var dayText = runAt.Date == today
            ? "今天"
            : runAt.Date == today.AddDays(1)
                ? "明天"
                : runAt.ToString("MM-dd");
        return $"{dayText} {runAt:HH:mm:ss}";
    }
}

public partial class ScheduleSettingsDialog : FluentWindow
{
    public ScheduledTaskSettings? Result { get; private set; }
    public ObservableCollection<ScheduleTaskEditorRow> Tasks { get; } = new();
    public IReadOnlyList<string> ProfileNames { get; }
    public IReadOnlyList<ScheduleModeOption> ModeOptions { get; } =
    [
        new(ScheduleRepeatMode.IntervalSeconds, "每隔几秒"),
        new(ScheduleRepeatMode.IntervalHours, "每隔几小时"),
        new(ScheduleRepeatMode.Daily, "每天固定时间"),
        new(ScheduleRepeatMode.Once, "只执行一次")
    ];

    private readonly string? _preferredProfileName;

    public ScheduleSettingsDialog(
        ScheduledTaskSettings current,
        IEnumerable<string> profileNames,
        string? preferredProfileName)
    {
        InitializeComponent();

        ProfileNames = profileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
        _preferredProfileName = preferredProfileName;

        foreach (var task in current.Tasks)
        {
            Tasks.Add(new ScheduleTaskEditorRow
            {
                Id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id,
                IsEnabled = task.IsEnabled,
                ProfileName = task.ProfileName,
                RepeatMode = task.RepeatMode,
                RunDate = task.RunDate == default ? DateTime.Today : task.RunDate.Date,
                TimeText = task.TimeText,
                IntervalHours = Math.Clamp(task.IntervalHours, 1, 720),
                IntervalSeconds = Math.Clamp(task.IntervalSeconds, 1, 86400),
                ActiveDurationMinutes = Math.Clamp(task.ActiveDurationMinutes, 0, 525600),
                IntervalStartedAt = task.IntervalStartedAt == default ? DateTime.Now : task.IntervalStartedAt,
                LastTriggeredAt = task.LastTriggeredAt
            });
        }

        DataContext = this;
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileNames.Count == 0)
        {
            MessageBox.Show(
                "请先新建一个方案，再添加定时执行。",
                "没有可用方案", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var nextTime = DateTime.Now.AddMinutes(5);
        Tasks.Add(new ScheduleTaskEditorRow
        {
            ProfileName = PickDefaultProfileName(),
            RepeatMode = ScheduleRepeatMode.IntervalHours,
            RunDate = nextTime.Date,
            TimeText = nextTime.ToString("HH:mm"),
            IntervalHours = 1,
            IntervalSeconds = 10,
            ActiveDurationMinutes = 0,
            IntervalStartedAt = DateTime.Now,
            IsEnabled = true
        });
    }

    private string PickDefaultProfileName()
    {
        if (!string.IsNullOrWhiteSpace(_preferredProfileName) &&
            ProfileNames.Contains(_preferredProfileName, StringComparer.OrdinalIgnoreCase))
        {
            return _preferredProfileName;
        }

        return ProfileNames.First();
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ScheduleTaskEditorRow row })
            Tasks.Remove(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var result = new ScheduledTaskSettings();

        foreach (var row in Tasks)
        {
            if (string.IsNullOrWhiteSpace(row.ProfileName) ||
                !ProfileNames.Contains(row.ProfileName, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "有定时任务没有选择有效方案，请先选择方案。",
                    "定时设置不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var time = TimeSpan.Zero;
            if (row.RepeatMode == ScheduleRepeatMode.IntervalHours)
            {
                if (row.IntervalHours is < 1 or > 720)
                {
                    MessageBox.Show(
                        "间隔小时请填写 1 到 720 之间的整数。",
                        "间隔时间错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (row.RepeatMode == ScheduleRepeatMode.IntervalSeconds)
            {
                if (row.IntervalSeconds is < 1 or > 86400)
                {
                    MessageBox.Show(
                        "间隔秒数请填写 1 到 86400 之间的整数。",
                        "间隔时间错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (!ScheduledTaskItem.TryParseTimeText(row.TimeText, out time))
            {
                MessageBox.Show(
                    "时间格式不正确，请用 09:30 这种格式填写。",
                    "时间格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (row.IsIntervalMode && row.ActiveDurationMinutes is < 0 or > 525600)
            {
                MessageBox.Show(
                    "持续运行时间请填写 0 到 525600 之间的整数分钟，0 表示不限时。",
                    "持续时间错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var runDate = row.RunDate == default ? DateTime.Today : row.RunDate.Date;
            if (row.IsEnabled && row.RepeatMode == ScheduleRepeatMode.Once)
            {
                var runAt = runDate.Add(time);
                if (runAt < DateTime.Now.AddSeconds(-30))
                {
                    MessageBox.Show(
                        $"方案「{row.ProfileName}」的一次执行时间已经过去，请改成未来时间。",
                        "定时时间已过", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var isIntervalMode = row.IsIntervalMode;
            result.Tasks.Add(new ScheduledTaskItem
            {
                Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id,
                IsEnabled = row.IsEnabled,
                ProfileName = row.ProfileName.Trim(),
                RepeatMode = row.RepeatMode,
                RunDate = runDate,
                TimeText = row.RepeatMode is ScheduleRepeatMode.IntervalHours or ScheduleRepeatMode.IntervalSeconds
                    ? row.TimeText
                    : ScheduledTaskItem.FormatTimeText(time),
                IntervalHours = Math.Clamp(row.IntervalHours, 1, 720),
                IntervalSeconds = Math.Clamp(row.IntervalSeconds, 1, 86400),
                ActiveDurationMinutes = Math.Clamp(row.ActiveDurationMinutes, 0, 525600),
                IntervalStartedAt = isIntervalMode
                    ? DateTime.Now
                    : row.IntervalStartedAt == default ? DateTime.Now : row.IntervalStartedAt,
                LastTriggeredAt = isIntervalMode ? null : row.LastTriggeredAt
            });
        }

        Result = result;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
