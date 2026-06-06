using System.Collections.ObjectModel;
using System.IO;
using AutoMacro.Models;
using AutoMacro.Services;
using AutoMacro.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SharpHook.Data;
using Application = System.Windows.Application;

namespace AutoMacro.ViewModels;

public partial class ActionListViewModel : ObservableObject
{
    private readonly IProfileManager _profileManager;
    private readonly IImageRecognitionService _imageRecognitionService;
    private readonly IOcrService _ocrService;
    private readonly IApiClientService _apiClientService;
    private RecordProfile? _currentProfile;

    [ObservableProperty]
    private ObservableCollection<InputEvent> _actions = new();

    [ObservableProperty]
    private InputEvent? _selectedAction;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public ActionListViewModel(
        IProfileManager profileManager,
        IImageRecognitionService imageRecognitionService,
        IOcrService ocrService,
        IApiClientService apiClientService)
    {
        _profileManager = profileManager;
        _imageRecognitionService = imageRecognitionService;
        _ocrService = ocrService;
        _apiClientService = apiClientService;
    }

    public void LoadFromProfile(RecordProfile profile)
    {
        _currentProfile = profile;
        var loaded = _profileManager.LoadActions(profile);
        Actions = new ObservableCollection<InputEvent>(loaded);
    }

    [RelayCommand]
    private void AddAction()
    {
        var newEvent = new InputEvent
        {
            EventType = InputEventType.Delay,
            DeltaMs = 100
        };

        if (SelectedIndex >= 0 && SelectedIndex < Actions.Count)
            Actions.Insert(SelectedIndex + 1, newEvent);
        else
            Actions.Add(newEvent);
    }

    [RelayCommand]
    private void AddKeyCombo()
    {
        var dialog = new KeyCaptureDialog
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedKeyCombo))
            return;

        var normalized = NormalizeKeyComboText(dialog.SelectedKeyCombo);
        var binding = HotkeySettings.ParseHotkey(normalized);
        if (!binding.IsValid)
        {
            System.Windows.MessageBox.Show(
                "这个按键暂时不支持。\n\n可以换一个常用按键，例如 Ctrl+A、Alt+F4、F5、Enter。",
                "按键操作",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var delaySeconds = PromptNumber("按键操作", "延迟执行时间：等多少秒后再按键？", 1);
        if (delaySeconds is null) return;

        var timeoutSeconds = PromptNumber("按键操作", "超时时间：最长允许这个按键操作执行多少秒？", 10);
        if (timeoutSeconds is null) return;

        var action = new InputEvent
        {
            EventType = InputEventType.KeyCombo,
            TextPattern = normalized,
            DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600),
            TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600)
        };

        InsertAction(action);
        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);
    }

    [RelayCommand]
    private void AddImageClick()
    {
        while (true)
        {
            var path = PickImage();
            if (path is null) return;

            var delaySeconds = PromptNumber("看到图片就点击", "延迟执行时间：图片出现后，等多少秒再点击？", 1);
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber("看到图片就点击", "超时时间：最长等待图片出现多少秒？", 10);
            if (timeoutSeconds is null) return;

            var action = new InputEvent
            {
                EventType = InputEventType.ImageClick,
                ImagePath = CopyImageToProfile(path),
                TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600),
                AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600),
                MatchThreshold = 0.92,
                DeltaMs = 100
            };

            InsertAction(action);
            SelectedAction = action;
            SelectedIndex = Actions.IndexOf(action);

            var continueAdd = System.Windows.MessageBox.Show(
                "是否继续添加下一张图片？\n点“是”会继续选择截图。",
                "继续添加",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (continueAdd != System.Windows.MessageBoxResult.Yes)
                break;
        }
    }

    [RelayCommand]
    private void AddWaitImage()
    {
        var path = PickImage();
        if (path is null) return;

        var timeoutSeconds = PromptNumber("等待图片出现", "最长等待图片出现多少秒：", 10);
        if (timeoutSeconds is null) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.WaitImage,
            ImagePath = CopyImageToProfile(path),
            TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600),
            MatchThreshold = 0.92,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddWaitText()
    {
        var text = PromptText("等待文字出现", "输入要等待出现的文字：");
        if (string.IsNullOrWhiteSpace(text)) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.WaitText,
            TextPattern = text.Trim(),
            TimeoutMs = 8000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddClickText()
    {
        var text = PromptText("看到文字就点击", "输入看到后要点击的文字：");
        if (string.IsNullOrWhiteSpace(text)) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.ClickText,
            TextPattern = text.Trim(),
            TimeoutMs = 8000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddReadText()
    {
        InsertAction(new InputEvent
        {
            EventType = InputEventType.ReadText,
            TimeoutMs = 3000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void SetStepTime()
    {
        if (SelectedAction is null)
        {
            System.Windows.MessageBox.Show(
                "请先选中一个要设置时间的步骤。",
                "时间设置",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (SelectedAction.EventType == InputEventType.ImageClick)
        {
            var delaySeconds = PromptNumber(
                "时间设置",
                "图片出现后，延迟多少秒再点击：",
                Math.Max(0, SelectedAction.AfterFoundDelayMs / 1000.0));
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber(
                "时间设置",
                "最长等待图片出现多少秒：",
                Math.Max(1, SelectedAction.TimeoutMs / 1000.0));
            if (timeoutSeconds is null) return;

            SelectedAction.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600);
            SelectedAction.AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
            return;
        }

        if (SelectedAction.EventType == InputEventType.KeyCombo)
        {
            var delaySeconds = PromptNumber(
                "时间设置",
                "延迟多少秒后执行按键：",
                Math.Max(0, SelectedAction.DeltaMs / 1000.0));
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber(
                "时间设置",
                "最长允许这个按键操作执行多少秒：",
                Math.Max(1, SelectedAction.TimeoutMs / 1000.0));
            if (timeoutSeconds is null) return;

            SelectedAction.DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
            SelectedAction.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600);
            return;
        }

        if (SelectedAction.EventType is InputEventType.WaitImage or InputEventType.WaitText or InputEventType.ClickText or InputEventType.ReadData or InputEventType.SubmitData or InputEventType.Notify)
        {
            var timeoutSeconds = PromptNumber(
                "时间设置",
                "最长等待多少秒：",
                Math.Max(1, SelectedAction.TimeoutMs / 1000.0));
            if (timeoutSeconds is null) return;

            SelectedAction.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600);
            return;
        }

        System.Windows.MessageBox.Show(
            "这个步骤暂时不需要单独设置时间。",
            "时间设置",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void EditTimeout(InputEvent? action)
    {
        if (action is null || !action.CanEditTimeout)
            return;

        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);

        var timeoutSeconds = PromptNumber(
            "修改超时时间",
            "超时时间：最长等待多少秒？",
            Math.Max(1, action.TimeoutMs / 1000.0));
        if (timeoutSeconds is null) return;

        action.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600);
    }

    [RelayCommand]
    private void EditAfterFoundDelay(InputEvent? action)
    {
        if (action is null || !action.CanEditAfterFoundDelay)
            return;

        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);

        var prompt = action.EventType == InputEventType.KeyCombo
            ? "延迟时间：等多少秒后执行按键？"
            : "延迟时间：图片出现后，等多少秒再点击？";
        var defaultValue = action.EventType == InputEventType.KeyCombo
            ? Math.Max(0, action.DeltaMs / 1000.0)
            : Math.Max(0, action.AfterFoundDelayMs / 1000.0);
        var delaySeconds = PromptNumber(
            "修改延迟时间",
            prompt,
            defaultValue);
        if (delaySeconds is null) return;

        if (action.EventType == InputEventType.KeyCombo)
            action.DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
        else
            action.AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
    }

    [RelayCommand]
    private void AddReadData()
    {
        var url = PromptText("读取数据", "输入要读取的网址：");
        if (string.IsNullOrWhiteSpace(url)) return;

        var variableName = PromptText("保存数据", "返回结果保存成什么名字：", "返回数据");
        if (string.IsNullOrWhiteSpace(variableName))
            variableName = "返回数据";

        InsertAction(new InputEvent
        {
            EventType = InputEventType.ReadData,
            RequestMethod = "GET",
            RequestUrl = url.Trim(),
            ResponseVariableName = variableName.Trim(),
            TimeoutMs = 10000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddSubmitData()
    {
        var url = PromptText("提交结果", "输入要提交到的网址：");
        if (string.IsNullOrWhiteSpace(url)) return;

        var body = PromptText("提交内容", "输入要提交的内容，可以使用 {{返回数据}}：", "{{返回数据}}");
        var variableName = PromptText("保存结果", "接口返回结果保存成什么名字：", "提交结果");
        if (string.IsNullOrWhiteSpace(variableName))
            variableName = "提交结果";

        InsertAction(new InputEvent
        {
            EventType = InputEventType.SubmitData,
            RequestMethod = "POST",
            RequestUrl = url.Trim(),
            RequestBody = body,
            ResponseVariableName = variableName.Trim(),
            TimeoutMs = 10000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddNotify()
    {
        var url = PromptText("完成后通知", "输入通知地址：");
        if (string.IsNullOrWhiteSpace(url)) return;

        var message = PromptText("通知内容", "输入通知内容：", "脚本执行完成");

        InsertAction(new InputEvent
        {
            EventType = InputEventType.Notify,
            RequestMethod = "POST",
            RequestUrl = url.Trim(),
            RequestBody = message,
            ResponseVariableName = "通知结果",
            TimeoutMs = 10000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void PickOcrRegion()
    {
        if (SelectedAction is null ||
            SelectedAction.EventType is not (InputEventType.WaitText or InputEventType.ClickText or InputEventType.ReadText))
        {
            System.Windows.MessageBox.Show(
                "请先选中一个文字识别步骤。",
                "框选区域",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var selector = new ScreenRegionSelectorWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (selector.ShowDialog() != true || selector.SelectedRegion is null)
            return;

        SelectedAction.UseOcrRegion = true;
        SelectedAction.OcrRegionX = selector.SelectedRegion.X;
        SelectedAction.OcrRegionY = selector.SelectedRegion.Y;
        SelectedAction.OcrRegionWidth = selector.SelectedRegion.Width;
        SelectedAction.OcrRegionHeight = selector.SelectedRegion.Height;

        System.Windows.MessageBox.Show(
            $"已保存识别范围：{selector.SelectedRegion.Width} x {selector.SelectedRegion.Height}",
            "框选完成",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task TestSelectedStep()
    {
        if (SelectedAction is null)
        {
            System.Windows.MessageBox.Show(
                "请先选中一个要测试的步骤。",
                "测试一下",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (SelectedAction.EventType is InputEventType.ImageClick or InputEventType.WaitImage)
        {
            await TestImageStep(SelectedAction);
            return;
        }

        if (SelectedAction.EventType is InputEventType.WaitText or InputEventType.ClickText or InputEventType.ReadText)
        {
            await TestOcrStep(SelectedAction);
            return;
        }

        if (SelectedAction.EventType == InputEventType.ReadData)
        {
            await TestReadDataStep(SelectedAction);
            return;
        }

        System.Windows.MessageBox.Show(
            "这个步骤暂时不需要测试。",
            "测试一下",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private async Task TestImageStep(InputEvent action)
    {
        if (string.IsNullOrWhiteSpace(action.ImagePath))
        {
            System.Windows.MessageBox.Show(
                "请先选中一个“看到图片就点击”或“等待图片出现”的步骤。",
                "测试图片",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(action.ImagePath))
        {
            System.Windows.MessageBox.Show(
                "这一步使用的图片文件不存在，请重新选择图片。",
                "测试图片",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            var timeoutMs = Math.Clamp(action.TimeoutMs <= 0 ? 3000 : action.TimeoutMs, 1000, 5000);
            using var cts = new CancellationTokenSource(timeoutMs + 1000);
            var result = await _imageRecognitionService.FindImageAsync(
                action.ImagePath,
                action.MatchThreshold,
                timeoutMs,
                cts.Token);

            if (result.Found)
            {
                System.Windows.MessageBox.Show(
                    $"找到了。\n\n会点击的位置：({result.X}, {result.Y})\n匹配度：{result.Score:0.000}",
                    "测试成功",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            System.Windows.MessageBox.Show(
                $"没找到这张图片。\n\n本次最高匹配度：{result.Score:0.000}\n可以确认图片是否在屏幕上，或重新截一张更清晰的图片。",
                "测试未通过",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "测试图片失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task TestOcrStep(InputEvent action)
    {
        try
        {
            var timeoutMs = Math.Clamp(action.TimeoutMs <= 0 ? 3000 : action.TimeoutMs, 1000, 5000);
            using var cts = new CancellationTokenSource(timeoutMs + 1000);
            var region = BuildOcrRegion(action);

            if (action.EventType is InputEventType.ReadText)
            {
                var read = await _ocrService.RecognizeScreenAsync(region, cts.Token);
                if (!read.Success)
                {
                    ShowOcrFailed(read.ErrorMessage);
                    return;
                }

                var text = string.IsNullOrWhiteSpace(read.Text) ? "没有识别到文字。" : read.Text;
                System.Windows.MessageBox.Show(
                    text,
                    "识别到的文字",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(action.TextPattern))
            {
                System.Windows.MessageBox.Show(
                    "这一步还没有填写要查找的文字。",
                    "测试文字",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var found = await _ocrService.FindTextAsync(action.TextPattern, timeoutMs, region, cts.Token);
            if (found.Found)
            {
                System.Windows.MessageBox.Show(
                    $"找到了。\n\n文字：{found.Text}\n位置：({found.X}, {found.Y})\n点击中心：({found.X + found.Width / 2}, {found.Y + found.Height / 2})",
                    "测试成功",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            System.Windows.MessageBox.Show(
                $"没找到这段文字。\n\n要找的文字：{action.TextPattern}\n\n屏幕上识别到的文字：\n{found.FullText}",
                "测试未通过",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowOcrFailed(ex.Message);
        }
    }

    private async Task TestReadDataStep(InputEvent action)
    {
        try
        {
            using var cts = new CancellationTokenSource(6000);
            var result = await _apiClientService.SendAsync("GET", action.RequestUrl ?? string.Empty, null, cts.Token);
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(
                    result.ErrorMessage,
                    "读取数据失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            System.Windows.MessageBox.Show(
                string.IsNullOrWhiteSpace(result.Body) ? "接口返回为空。" : result.Body,
                "读取到的数据",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "读取数据失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private static void ShowOcrFailed(string message)
    {
        System.Windows.MessageBox.Show(
            message,
            "识别文字失败",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    [RelayCommand]
    private void AddWaitWindow()
    {
        var title = PromptText("等待窗口", "输入窗口标题里包含的字：");
        if (string.IsNullOrWhiteSpace(title)) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.WaitWindow,
            WindowTitle = title.Trim(),
            TimeoutMs = 8000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddActivateWindow()
    {
        var title = PromptText("切换到窗口", "输入窗口标题里包含的字：");
        if (string.IsNullOrWhiteSpace(title)) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.ActivateWindow,
            WindowTitle = title.Trim(),
            TimeoutMs = 3000,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void DeleteAction()
    {
        if (SelectedAction is not null)
            Actions.Remove(SelectedAction);
    }

    public void DeleteAction(InputEvent action)
    {
        if (!Actions.Contains(action))
            return;

        var index = Actions.IndexOf(action);
        Actions.Remove(action);

        if (Actions.Count == 0)
        {
            SelectedAction = null;
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Min(index, Actions.Count - 1);
        SelectedAction = Actions[SelectedIndex];
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Actions.Count == 0) return;

        var result = System.Windows.MessageBox.Show(
            "确定要清空所有脚本步骤吗？\n清空后可以重新录制。",
            "清空脚本步骤",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        Actions.Clear();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedIndex > 0)
        {
            var idx = SelectedIndex;
            var item = Actions[idx];
            Actions.RemoveAt(idx);
            Actions.Insert(idx - 1, item);
            SelectedIndex = idx - 1;
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Actions.Count - 1)
        {
            var idx = SelectedIndex;
            var item = Actions[idx];
            Actions.RemoveAt(idx);
            Actions.Insert(idx + 1, item);
            SelectedIndex = idx + 1;
        }
    }

    [RelayCommand]
    private void SaveActions()
    {
        if (_currentProfile is null)
        {
            System.Windows.MessageBox.Show(
                "请先选择一个方案，再保存。",
                "全局保存",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            _profileManager.SaveProfile(_currentProfile);
            _profileManager.SaveActions(_currentProfile, Actions.ToList());

            System.Windows.MessageBox.Show(
                "已全局保存。\n\n当前方案设置和脚本步骤已经保存。",
                "全局保存成功",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "全局保存失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void InsertAction(InputEvent evt)
    {
        if (SelectedIndex >= 0 && SelectedIndex < Actions.Count)
            Actions.Insert(SelectedIndex + 1, evt);
        else
            Actions.Add(evt);
    }

    private string? PickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要查找的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private string CopyImageToProfile(string sourcePath)
    {
        if (_currentProfile is null)
            return sourcePath;

        var imageDir = Path.Combine(_currentProfile.FolderPath, "Images");
        Directory.CreateDirectory(imageDir);

        var ext = Path.GetExtension(sourcePath);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{ext}";
        var destination = Path.Combine(imageDir, fileName);
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static OcrRegion? BuildOcrRegion(InputEvent action)
    {
        return action.UseOcrRegion && action.OcrRegionWidth > 0 && action.OcrRegionHeight > 0
            ? new OcrRegion(action.OcrRegionX, action.OcrRegionY, action.OcrRegionWidth, action.OcrRegionHeight)
            : null;
    }

    private static string PromptText(string title, string prompt, string defaultValue = "")
    {
        return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, defaultValue);
    }

    private static string NormalizeKeyComboText(string text)
    {
        var parts = text
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (parts.Count == 0)
            return string.Empty;

        var modifiers = new List<string>();
        string? mainKey = null;

        foreach (var part in parts)
        {
            var normalized = part.Trim().ToUpperInvariant() switch
            {
                "CONTROL" => "CTRL",
                "CTRL" => "CTRL",
                "SHIFT" => "SHIFT",
                "ALT" => "ALT",
                "OPTION" => "ALT",
                _ => part.Trim().ToUpperInvariant()
            };

            switch (normalized)
            {
                case "CTRL":
                    AddUnique(modifiers, "Ctrl");
                    break;
                case "SHIFT":
                    AddUnique(modifiers, "Shift");
                    break;
                case "ALT":
                    AddUnique(modifiers, "Alt");
                    break;
                default:
                    mainKey = normalized switch
                    {
                        "ESCAPE" => "Escape",
                        "ESC" => "Esc",
                        "ENTER" => "Enter",
                        "RETURN" => "Enter",
                        "TAB" => "Tab",
                        "SPACE" => "Space",
                        "DELETE" => "Delete",
                        "BACKSPACE" => "Backspace",
                        "CAPSLOCK" => "CapsLock",
                        "CAPS LOCK" => "CapsLock",
                        _ => normalized
                    };
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(mainKey))
            return text.Trim();

        modifiers.Add(mainKey);
        return string.Join("+", modifiers);
    }

    private static void AddUnique(ICollection<string> values, string value)
    {
        if (!values.Contains(value))
            values.Add(value);
    }

    private static double? PromptNumber(string title, string prompt, double defaultValue)
    {
        var text = PromptText(title, prompt, defaultValue.ToString("0.###"));
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (double.TryParse(text.Trim(), out var value) && value >= 0)
            return value;

        System.Windows.MessageBox.Show(
            "请输入有效的数字。",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        return null;
    }

    private static int SecondsToMilliseconds(double seconds, int minSeconds, int maxSeconds)
    {
        var clamped = Math.Clamp(seconds, minSeconds, maxSeconds);
        return (int)Math.Round(clamped * 1000);
    }
}
