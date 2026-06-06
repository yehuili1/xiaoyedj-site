using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AutoMacro.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloseBehavior
{
    AskEveryTime,
    ExitApp,
    MinimizeToTray
}

public class AppSettings
{
    private const string StartupRunName = "AutoMacro_QuanNengScript";

    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.AskEveryTime;
    public bool StartWithWindows { get; set; }

    public static readonly string SettingsFilePath =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            var defaults = new AppSettings
            {
                StartWithWindows = IsStartupEnabled()
            };
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.StartWithWindows = IsStartupEnabled();
            return settings;
        }
        catch
        {
            return new AppSettings
            {
                StartWithWindows = IsStartupEnabled()
            };
        }
    }

    public void Save()
    {
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void ApplyStartupSetting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        if (key is null)
            return;

        if (StartWithWindows)
        {
            var exePath = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exePath))
                key.SetValue(StartupRunName, $"\"{exePath}\"");
            return;
        }

        key.DeleteValue(StartupRunName, throwOnMissingValue: false);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: false);
        return key?.GetValue(StartupRunName) is not null;
    }
}
