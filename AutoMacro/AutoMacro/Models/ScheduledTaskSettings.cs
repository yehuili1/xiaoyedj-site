using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoMacro.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleRepeatMode
{
    Once,
    Daily,
    IntervalHours,
    IntervalSeconds
}

public class ScheduledTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsEnabled { get; set; } = true;
    public string ProfileName { get; set; } = string.Empty;
    public ScheduleRepeatMode RepeatMode { get; set; } = ScheduleRepeatMode.Daily;
    public DateTime RunDate { get; set; } = DateTime.Today;
    public string TimeText { get; set; } = "09:00";
    public int IntervalHours { get; set; } = 1;
    public int IntervalSeconds { get; set; } = 10;
    public DateTime IntervalStartedAt { get; set; } = DateTime.Now;
    public DateTime? LastTriggeredAt { get; set; }

    public bool IsDue(DateTime now)
    {
        if (!IsEnabled)
            return false;

        if (IsIntervalMode)
        {
            var nextRun = GetNextRunAfter(now);
            return nextRun is not null && nextRun.Value <= now;
        }

        if (!IsEnabled || !TryGetScheduledRunAt(now, out var runAt))
            return false;

        if (now < runAt || now >= runAt.AddMinutes(1))
            return false;

        return RepeatMode switch
        {
            ScheduleRepeatMode.Daily => LastTriggeredAt?.Date != now.Date,
            ScheduleRepeatMode.Once => LastTriggeredAt is null,
            _ => false
        };
    }

    public DateTime? GetNextRunAfter(DateTime now)
    {
        if (!IsEnabled)
            return null;

        if (IsIntervalMode)
        {
            var baseAt = LastTriggeredAt ?? IntervalStartedAt;
            var nextRun = RepeatMode == ScheduleRepeatMode.IntervalSeconds
                ? baseAt.AddSeconds(Math.Clamp(IntervalSeconds, 1, 86400))
                : baseAt.AddHours(Math.Clamp(IntervalHours, 1, 720));
            return nextRun <= now ? now : nextRun;
        }

        if (!IsEnabled || !TryParseTimeText(TimeText, out var time))
            return null;

        if (RepeatMode == ScheduleRepeatMode.Once)
        {
            var onceAt = RunDate.Date.Add(time);
            return onceAt >= now ? onceAt : null;
        }

        var todayAt = now.Date.Add(time);
        if (todayAt >= now && LastTriggeredAt?.Date != now.Date)
            return todayAt;

        return todayAt.AddDays(1);
    }

    public void MarkTriggered(DateTime now)
    {
        LastTriggeredAt = now;
        if (RepeatMode == ScheduleRepeatMode.Once)
            IsEnabled = false;
    }

    public bool TryGetScheduledRunAt(DateTime now, out DateTime runAt)
    {
        runAt = default;

        if (IsIntervalMode)
        {
            runAt = GetNextRunAfter(now) ?? default;
            return runAt != default;
        }

        if (!TryParseTimeText(TimeText, out var time))
            return false;

        runAt = RepeatMode == ScheduleRepeatMode.Once
            ? RunDate.Date.Add(time)
            : now.Date.Add(time);
        return true;
    }

    public static bool TryParseTimeText(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!DateTime.TryParseExact(
                text.Trim(),
                new[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return false;

        time = parsed.TimeOfDay;
        return true;
    }

    public static string FormatTimeText(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    [JsonIgnore]
    private bool IsIntervalMode =>
        RepeatMode is ScheduleRepeatMode.IntervalHours or ScheduleRepeatMode.IntervalSeconds;
}

public class ScheduledTaskSettings
{
    public List<ScheduledTaskItem> Tasks { get; set; } = new();

    public static readonly string SettingsFilePath =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, "schedule_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ScheduledTaskSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            var defaults = new ScheduledTaskSettings();
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<ScheduledTaskSettings>(json, JsonOptions)
                           ?? new ScheduledTaskSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new ScheduledTaskSettings();
        }
    }

    public void Save()
    {
        Normalize();
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void Normalize()
    {
        foreach (var task in Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
                task.Id = Guid.NewGuid().ToString("N");

            if (task.RunDate == default)
                task.RunDate = DateTime.Today;

            task.IntervalHours = Math.Clamp(task.IntervalHours, 1, 720);
            task.IntervalSeconds = Math.Clamp(task.IntervalSeconds, 1, 86400);
            if (task.IntervalStartedAt == default)
                task.IntervalStartedAt = DateTime.Now;

            if (!ScheduledTaskItem.TryParseTimeText(task.TimeText, out var time))
                task.TimeText = "09:00";
            else
                task.TimeText = ScheduledTaskItem.FormatTimeText(time);
        }
    }
}
