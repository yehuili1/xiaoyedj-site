using System.IO;
using System.Text.Json;
using SharpHook.Data;

namespace AutoMacro.Models;

/// <summary>
/// 解析后的快捷键绑定：主键 + 修饰键组合
/// </summary>
public record struct HotkeyBinding(KeyCode Key, bool Ctrl, bool Shift, bool Alt)
{
    public static readonly HotkeyBinding Empty = new(KeyCode.VcUndefined, false, false, false);
    public bool IsValid => Key != KeyCode.VcUndefined;
}

public class HotkeySettings
{
    public string StartStopRecording { get; set; } = "F10";
    public string PauseRecording { get; set; } = "F8";
    public string EmergencyStop { get; set; } = "F11";
    public string StartPlayback { get; set; } = "F12";

    /// <summary>
    /// 方案专属快捷键：方案名 → 快捷键字符串（如 "Ctrl+F1"）
    /// </summary>
    public Dictionary<string, string> ProfileHotkeys { get; set; } = new();

    private static readonly string SettingsPath =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, "hotkey_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public static HotkeySettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new HotkeySettings();
            defaults.Save();
            return defaults;
        }

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<HotkeySettings>(json, JsonOptions) ?? new HotkeySettings();
    }

    /// <summary>
    /// 将字符串如 "F9" 转换为 SharpHook KeyCode
    /// </summary>
    public static KeyCode ParseKeyCode(string name)
    {
        var key = name.ToUpperInvariant();
        return key switch
        {
            // 字母 A-Z
            "A" => KeyCode.VcA, "B" => KeyCode.VcB, "C" => KeyCode.VcC,
            "D" => KeyCode.VcD, "E" => KeyCode.VcE, "F" => KeyCode.VcF,
            "G" => KeyCode.VcG, "H" => KeyCode.VcH, "I" => KeyCode.VcI,
            "J" => KeyCode.VcJ, "K" => KeyCode.VcK, "L" => KeyCode.VcL,
            "M" => KeyCode.VcM, "N" => KeyCode.VcN, "O" => KeyCode.VcO,
            "P" => KeyCode.VcP, "Q" => KeyCode.VcQ, "R" => KeyCode.VcR,
            "S" => KeyCode.VcS, "T" => KeyCode.VcT, "U" => KeyCode.VcU,
            "V" => KeyCode.VcV, "W" => KeyCode.VcW, "X" => KeyCode.VcX,
            "Y" => KeyCode.VcY, "Z" => KeyCode.VcZ,

            // 数字 0-9
            "0" => KeyCode.Vc0, "1" => KeyCode.Vc1, "2" => KeyCode.Vc2,
            "3" => KeyCode.Vc3, "4" => KeyCode.Vc4, "5" => KeyCode.Vc5,
            "6" => KeyCode.Vc6, "7" => KeyCode.Vc7, "8" => KeyCode.Vc8,
            "9" => KeyCode.Vc9,

            // F1-F12
            "F1" => KeyCode.VcF1, "F2" => KeyCode.VcF2, "F3" => KeyCode.VcF3,
            "F4" => KeyCode.VcF4, "F5" => KeyCode.VcF5, "F6" => KeyCode.VcF6,
            "F7" => KeyCode.VcF7, "F8" => KeyCode.VcF8, "F9" => KeyCode.VcF9,
            "F10" => KeyCode.VcF10, "F11" => KeyCode.VcF11, "F12" => KeyCode.VcF12,

            // 小键盘
            "NUM0" => KeyCode.VcNumPad0, "NUM1" => KeyCode.VcNumPad1, "NUM2" => KeyCode.VcNumPad2,
            "NUM3" => KeyCode.VcNumPad3, "NUM4" => KeyCode.VcNumPad4, "NUM5" => KeyCode.VcNumPad5,
            "NUM6" => KeyCode.VcNumPad6, "NUM7" => KeyCode.VcNumPad7, "NUM8" => KeyCode.VcNumPad8,
            "NUM9" => KeyCode.VcNumPad9,
            "NUM*" => KeyCode.VcNumPadMultiply, "NUM+" => KeyCode.VcNumPadAdd,
            "NUM-" => KeyCode.VcNumPadSubtract, "NUM." => KeyCode.VcNumPadSeparator,
            "NUM/" => KeyCode.VcNumPadDivide,

            // 导航 / 特殊
            "SPACE" => KeyCode.VcSpace,
            "BACKSPACE" => KeyCode.VcBackspace,
            "DELETE" => KeyCode.VcDelete,
            "PAUSE" => KeyCode.VcPause,
            "SCROLLLOCK" => KeyCode.VcScrollLock,
            "INSERT" => KeyCode.VcInsert,
            "HOME" => KeyCode.VcHome,
            "END" => KeyCode.VcEnd,
            "PAGEUP" => KeyCode.VcPageUp,
            "PAGEDOWN" => KeyCode.VcPageDown,
            "UP" => KeyCode.VcUp,
            "DOWN" => KeyCode.VcDown,
            "LEFT" => KeyCode.VcLeft,
            "RIGHT" => KeyCode.VcRight,

            // 符号
            "`" => KeyCode.VcBackQuote,
            "-" => KeyCode.VcMinus,
            "=" => KeyCode.VcEquals,
            "[" => KeyCode.VcOpenBracket,
            "]" => KeyCode.VcCloseBracket,
            "\\" => KeyCode.VcBackslash,
            ";" => KeyCode.VcSemicolon,
            "'" => KeyCode.VcQuote,
            "," => KeyCode.VcComma,
            "." => KeyCode.VcPeriod,
            "/" => KeyCode.VcSlash,

            _ => KeyCode.VcUndefined
        };
    }

    /// <summary>
    /// 解析快捷键字符串（如 "Ctrl+Shift+F1", "F10"）为 HotkeyBinding
    /// </summary>
    public static HotkeyBinding ParseHotkey(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return HotkeyBinding.Empty;

        bool ctrl = false, shift = false, alt = false;
        var parts = hotkeyString.Split('+');

        foreach (var part in parts[..^1]) // 除最后一个外都是修饰键
        {
            switch (part.Trim().ToUpperInvariant())
            {
                case "CTRL": ctrl = true; break;
                case "SHIFT": shift = true; break;
                case "ALT": alt = true; break;
            }
        }

        var mainKey = ParseKeyCode(parts[^1].Trim());
        return new HotkeyBinding(mainKey, ctrl, shift, alt);
    }

    public static bool IsSupportedHotkey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return ParseHotkey(name).IsValid;
    }

    public static string KeyCodeToString(KeyCode code)
    {
        return code switch
        {
            KeyCode.VcF1 => "F1", KeyCode.VcF2 => "F2", KeyCode.VcF3 => "F3",
            KeyCode.VcF4 => "F4", KeyCode.VcF5 => "F5", KeyCode.VcF6 => "F6",
            KeyCode.VcF7 => "F7", KeyCode.VcF8 => "F8", KeyCode.VcF9 => "F9",
            KeyCode.VcF10 => "F10", KeyCode.VcF11 => "F11", KeyCode.VcF12 => "F12",
            KeyCode.VcPause => "Pause", KeyCode.VcScrollLock => "ScrollLock",
            KeyCode.VcInsert => "Insert", KeyCode.VcHome => "Home",
            KeyCode.VcEnd => "End", KeyCode.VcPageUp => "PageUp",
            KeyCode.VcPageDown => "PageDown",
            _ => code.ToString()
        };
    }
}
