using System.IO;
using System.Text.Json.Serialization;
using AutoMacro.Services;

namespace AutoMacro.Models;

public enum MonitorTriggerType
{
    Image,
    Text,
    Window
}

public class MonitorTriggerSettings
{
    public MonitorTriggerType TriggerType { get; set; } = MonitorTriggerType.Image;
    public string ImagePath { get; set; } = string.Empty;
    public string TextPattern { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public bool UseRegion { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    public double MatchThreshold { get; set; } = 0.92;
    public int ScanIntervalMs { get; set; } = 800;
    public int ConsecutiveMatches { get; set; } = 2;
    public int CooldownSeconds { get; set; } = 10;

    [JsonIgnore]
    public bool IsConfigured => TriggerType switch
    {
        MonitorTriggerType.Image => !string.IsNullOrWhiteSpace(ImagePath),
        MonitorTriggerType.Text => !string.IsNullOrWhiteSpace(TextPattern),
        MonitorTriggerType.Window => !string.IsNullOrWhiteSpace(WindowTitle),
        _ => false
    };

    public OcrRegion? GetRegion()
    {
        return UseRegion && RegionWidth > 0 && RegionHeight > 0
            ? new OcrRegion(RegionX, RegionY, RegionWidth, RegionHeight)
            : null;
    }

    [JsonIgnore]
    public string TriggerTypeText => TriggerType switch
    {
        MonitorTriggerType.Image => "图片",
        MonitorTriggerType.Text => "文字",
        MonitorTriggerType.Window => "窗口",
        _ => "未设置"
    };

    [JsonIgnore]
    public string TargetText => TriggerType switch
    {
        MonitorTriggerType.Image => string.IsNullOrWhiteSpace(ImagePath)
            ? "尚未选择目标图片"
            : Path.GetFileName(ImagePath),
        MonitorTriggerType.Text => string.IsNullOrWhiteSpace(TextPattern)
            ? "尚未填写目标文字"
            : TextPattern,
        MonitorTriggerType.Window => string.IsNullOrWhiteSpace(WindowTitle)
            ? "尚未填写窗口标题"
            : WindowTitle,
        _ => "尚未配置"
    };

    [JsonIgnore]
    public string RegionText => GetRegion() is { } region
        ? $"区域 {region.Width}×{region.Height}，位置 ({region.X}, {region.Y})"
        : "整个屏幕";

    public MonitorTriggerSettings Clone()
    {
        return new MonitorTriggerSettings
        {
            TriggerType = TriggerType,
            ImagePath = ImagePath,
            TextPattern = TextPattern,
            WindowTitle = WindowTitle,
            UseRegion = UseRegion,
            RegionX = RegionX,
            RegionY = RegionY,
            RegionWidth = RegionWidth,
            RegionHeight = RegionHeight,
            MatchThreshold = MatchThreshold,
            ScanIntervalMs = ScanIntervalMs,
            ConsecutiveMatches = ConsecutiveMatches,
            CooldownSeconds = CooldownSeconds
        };
    }

    public void Normalize()
    {
        MatchThreshold = Math.Clamp(MatchThreshold, 0.5, 1.0);
        ScanIntervalMs = Math.Clamp(ScanIntervalMs, 300, 5000);
        ConsecutiveMatches = Math.Clamp(ConsecutiveMatches, 1, 5);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 3600);

        if (RegionWidth <= 0 || RegionHeight <= 0)
        {
            UseRegion = false;
            RegionX = 0;
            RegionY = 0;
            RegionWidth = 0;
            RegionHeight = 0;
        }
    }
}
