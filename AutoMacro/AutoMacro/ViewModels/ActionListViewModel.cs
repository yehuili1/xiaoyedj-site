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
    private readonly IRecordingService _recordingService;
    private RecordProfile? _currentProfile;
    private SegmentRecordingDialog? _segmentRecordingDialog;

    [ObservableProperty]
    private ObservableCollection<InputEvent> _actions = new();

    [ObservableProperty]
    private InputEvent? _selectedAction;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public bool IsRecordingSegment { get; private set; }
    public bool IsInsertRecordingDialogOpen => _segmentRecordingDialog is not null;

    public ActionListViewModel(
        IProfileManager profileManager,
        IImageRecognitionService imageRecognitionService,
        IOcrService ocrService,
        IApiClientService apiClientService,
        IRecordingService recordingService)
    {
        _profileManager = profileManager;
        _imageRecognitionService = imageRecognitionService;
        _ocrService = ocrService;
        _apiClientService = apiClientService;
        _recordingService = recordingService;
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
    private void InsertRecording()
    {
        ShowInsertRecordingDialog();
    }

    public void HandleInsertRecordingHotkey()
    {
        if (_segmentRecordingDialog is not null)
        {
            _segmentRecordingDialog.TriggerHotkey();
            return;
        }

        ShowInsertRecordingDialog();
    }

    private void ShowInsertRecordingDialog()
    {
        if (_recordingService.IsRecording)
        {
            System.Windows.MessageBox.Show(
                "当前已经在录制中，请先停止录制。",
                "插入录制",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var insertIndex = SelectedIndex >= 0 && SelectedIndex < Actions.Count
            ? SelectedIndex + 1
            : Actions.Count;

        var dialog = new SegmentRecordingDialog
        {
            Owner = Application.Current.MainWindow
        };
        _segmentRecordingDialog = dialog;

        var started = false;
        dialog.StartRequested += (_, _) =>
        {
            IsRecordingSegment = true;
            _recordingService.StartRecording();
            started = true;
        };
        dialog.StopRequested += (_, _) =>
        {
            if (_recordingService.IsRecording)
                _recordingService.StopRecording();
        };

        bool? result;
        try
        {
            result = dialog.ShowDialog();
        }
        finally
        {
            _segmentRecordingDialog = null;
            if (_recordingService.IsRecording)
                _recordingService.StopRecording();

            IsRecordingSegment = false;
        }

        if (result != true || !started)
            return;

        var recorded = NormalizeRecordedEvents(_recordingService.GetRecordedEvents());
        if (recorded.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "没有录到鼠标或键盘动作。",
                "插入录制",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var delaySeconds = PromptNumber("插入录制", "延迟执行时间：等多少秒后开始执行这段录制？", 1);
        if (delaySeconds is null) return;

        var timeoutSeconds = PromptNumber("插入录制", "超时时间：最长允许这段录制执行多少秒？", 10);
        if (timeoutSeconds is null) return;

        var segment = new InputEvent
        {
            EventType = InputEventType.RecordedSegment,
            DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600),
            TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 1, 600),
            RecordedEvents = recorded
        };

        Actions.Insert(insertIndex, segment);

        SelectedIndex = insertIndex;
        SelectedAction = segment;

        System.Windows.MessageBox.Show(
            $"已插入 1 条鼠标键盘录制任务。\n里面包含 {recorded.Count} 个动作。\n\n记得点击“全局保存”。",
            "插入录制完成",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
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
    private void AddPasteText()
    {
        var dialog = new PasteTextDialog
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.TextContent))
            return;

        var delaySeconds = PromptNumber("插入文字", "延迟执行时间：等多少秒后粘贴文字？", 1);
        if (delaySeconds is null) return;

        var action = new InputEvent
        {
            EventType = InputEventType.PasteText,
            TextPattern = dialog.TextContent,
            DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600)
        };

        InsertAction(action);
        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);
    }

    [RelayCommand]
    private void AddImageClick()
    {
        AddImageLocateAction(
            InputEventType.ImageClick,
            "看到图片就点击",
            "延迟执行时间：图片出现后，等多少秒再点击？",
            "是否继续添加下一张点击图片？\n点“是”会继续选择截图。");
    }

    [RelayCommand]
    private void AddImageMove()
    {
        AddImageLocateAction(
            InputEventType.ImageMove,
            "看到图片就移动",
            "延迟执行时间：图片出现后，等多少秒再移动鼠标？",
            "是否继续添加下一张移动图片？\n点“是”会继续选择截图。");
    }

    private void AddImageLocateAction(
        InputEventType eventType,
        string title,
        string delayPrompt,
        string continuePrompt)
    {
        while (true)
        {
            var path = PickImage();
            if (path is null) return;

            var delaySeconds = PromptNumber(title, delayPrompt, 1);
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber(title, "超时时间：最长等待图片出现多少秒？", 10);
            if (timeoutSeconds is null) return;

            var action = new InputEvent
            {
                EventType = eventType,
                ImagePath = CopyImageToProfile(path),
                TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 0, 600),
                AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600),
                MatchThreshold = 0.92,
                DeltaMs = 0
            };

            InsertAction(action);
            SelectedAction = action;
            SelectedIndex = Actions.IndexOf(action);

            var continueAdd = System.Windows.MessageBox.Show(
                continuePrompt,
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

        if (SelectedAction.EventType is InputEventType.ImageClick or InputEventType.ImageMove)
        {
            var isMove = SelectedAction.EventType == InputEventType.ImageMove;
            var delaySeconds = PromptNumber(
                "时间设置",
                isMove ? "图片出现后，延迟多少秒再移动鼠标：" : "图片出现后，延迟多少秒再点击：",
                Math.Max(0, SelectedAction.AfterFoundDelayMs / 1000.0));
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber(
                "时间设置",
                "最长等待图片出现多少秒：",
                Math.Max(0, SelectedAction.TimeoutMs / 1000.0));
            if (timeoutSeconds is null) return;

            SelectedAction.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, 0, 600);
            SelectedAction.AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
            return;
        }

        if (SelectedAction.EventType is InputEventType.KeyCombo or InputEventType.RecordedSegment)
        {
            var isSegment = SelectedAction.EventType == InputEventType.RecordedSegment;
            var delaySeconds = PromptNumber(
                "时间设置",
                isSegment ? "延迟多少秒后开始执行这段录制？" : "延迟多少秒后执行按键：",
                Math.Max(0, SelectedAction.DeltaMs / 1000.0));
            if (delaySeconds is null) return;

            var timeoutSeconds = PromptNumber(
                "时间设置",
                isSegment ? "最长允许这段录制执行多少秒？" : "最长允许这个按键操作执行多少秒：",
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
            Math.Max(0, action.TimeoutMs / 1000.0));
        if (timeoutSeconds is null) return;

        var minTimeoutSeconds = action.EventType is InputEventType.ImageClick or InputEventType.ImageMove
            ? 0
            : 1;
        action.TimeoutMs = SecondsToMilliseconds(timeoutSeconds.Value, minTimeoutSeconds, 600);
    }

    [RelayCommand]
    private void EditAfterFoundDelay(InputEvent? action)
    {
        if (action is null || !action.CanEditAfterFoundDelay)
            return;

        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);

        var usesDeltaDelay = action.EventType is InputEventType.KeyCombo or InputEventType.RecordedSegment
            or InputEventType.PasteText;
        var prompt = action.EventType switch
        {
            InputEventType.KeyCombo => "延迟时间：等多少秒后执行按键？",
            InputEventType.RecordedSegment => "延迟时间：等多少秒后开始执行这段录制？",
            InputEventType.PasteText => "延迟时间：等多少秒后粘贴文字？",
            InputEventType.ImageMove => "延迟时间：图片出现后，等多少秒再移动鼠标？",
            _ => "延迟时间：图片出现后，等多少秒再点击？"
        };
        var defaultValue = usesDeltaDelay
            ? Math.Max(0, action.DeltaMs / 1000.0)
            : Math.Max(0, action.AfterFoundDelayMs / 1000.0);
        var delaySeconds = PromptNumber(
            "修改延迟时间",
            prompt,
            defaultValue);
        if (delaySeconds is null) return;

        if (usesDeltaDelay)
            action.DeltaMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
        else
            action.AfterFoundDelayMs = SecondsToMilliseconds(delaySeconds.Value, 0, 600);
    }

    public void EditAction(InputEvent action)
    {
        if (!Actions.Contains(action))
            return;

        SelectedAction = action;
        SelectedIndex = Actions.IndexOf(action);

        switch (action.EventType)
        {
            case InputEventType.PasteText:
                EditPasteTextAction(action);
                return;

            case InputEventType.ImageClick:
            case InputEventType.ImageMove:
            case InputEventType.WaitImage:
                EditImageAction(action);
                return;

            case InputEventType.KeyCombo:
                EditKeyComboAction(action);
                return;

            case InputEventType.WaitText:
            case InputEventType.ClickText:
                EditTextPatternAction(action);
                return;

            case InputEventType.WaitWindow:
            case InputEventType.ActivateWindow:
                EditWindowTitleAction(action);
                return;

            case InputEventType.ReadData:
            case InputEventType.SubmitData:
            case InputEventType.Notify:
                EditApiAction(action);
                return;

            default:
                System.Windows.MessageBox.Show(
                    "这个动作暂时没有可直接修改的内容。\n\n可以右键删除后重新添加，或者直接点延迟时间、超时时间修改。",
                    "修改这一步",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
        }
    }

    private static void EditPasteTextAction(InputEvent action)
    {
        var dialog = new PasteTextDialog(action.TextPattern)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.TextContent))
            action.TextPattern = dialog.TextContent;
    }

    private void EditImageAction(InputEvent action)
    {
        var path = PickImage();
        if (path is null)
            return;

        action.ImagePath = CopyImageToProfile(path);
        if (action.MatchThreshold <= 0)
            action.MatchThreshold = 0.92;
    }

    private static void EditKeyComboAction(InputEvent action)
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
                "修改按键操作",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        action.TextPattern = normalized;
    }

    private static void EditTextPatternAction(InputEvent action)
    {
        var title = action.EventType == InputEventType.WaitText
            ? "修改等待文字"
            : "修改点击文字";
        var prompt = action.EventType == InputEventType.WaitText
            ? "输入要等待出现的文字："
            : "输入看到后要点击的文字：";

        var text = PromptText(title, prompt, action.TextPattern ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(text))
            action.TextPattern = text.Trim();
    }

    private static void EditWindowTitleAction(InputEvent action)
    {
        var title = action.EventType == InputEventType.WaitWindow
            ? "修改等待窗口"
            : "修改切换窗口";
        var text = PromptText(title, "输入窗口标题里包含的字：", action.WindowTitle ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(text))
            action.WindowTitle = text.Trim();
    }

    private static void EditApiAction(InputEvent action)
    {
        var url = PromptText("修改网址", "输入网址：", action.RequestUrl ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(url))
            action.RequestUrl = url.Trim();

        if (action.EventType == InputEventType.SubmitData)
        {
            var body = PromptText("修改提交内容", "输入提交内容：", action.RequestBody ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(body))
                action.RequestBody = body;
        }

        if (action.EventType == InputEventType.Notify)
        {
            var message = PromptText("修改通知内容", "输入通知内容：", action.RequestBody ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(message))
                action.RequestBody = message;
        }
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

        if (SelectedAction.EventType is InputEventType.ImageClick or InputEventType.ImageMove or InputEventType.WaitImage)
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
                "请先选中一个“看到图片就点击”“看到图片就移动”或“等待图片出现”的步骤。",
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
                var locationText = action.EventType switch
                {
                    InputEventType.ImageClick => "会点击的位置",
                    InputEventType.ImageMove => "会移动到的位置",
                    _ => "找到的位置"
                };
                System.Windows.MessageBox.Show(
                    $"找到了。\n\n{locationText}：({result.X}, {result.Y})\n匹配度：{result.Score:0.000}",
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

    public void MoveAction(InputEvent action, int newIndex)
    {
        var oldIndex = Actions.IndexOf(action);
        if (oldIndex < 0 || Actions.Count == 0)
            return;

        newIndex = Math.Clamp(newIndex, 0, Actions.Count - 1);
        if (oldIndex == newIndex)
            return;

        Actions.Move(oldIndex, newIndex);
        SelectedAction = action;
        SelectedIndex = newIndex;
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

    private static IEnumerable<InputEvent> TrimOwnAppEvents(IList<InputEvent> events)
    {
        var start = 0;
        var end = events.Count - 1;

        while (start <= end && IsOwnAppMouseEvent(events[start]))
            start++;

        while (end >= start && IsOwnAppMouseEvent(events[end]))
            end--;

        for (var i = start; i <= end; i++)
            yield return events[i];
    }

    private static List<InputEvent> NormalizeRecordedEvents(IList<InputEvent> events)
    {
        var normalized = new List<InputEvent>();
        var pressedButtons = new HashSet<int>();
        var orphanMoves = new List<InputEvent>();
        long pendingDelta = 0;

        foreach (var evt in TrimOwnAppEvents(events))
        {
            switch (evt.EventType)
            {
                case InputEventType.MouseDown:
                    pendingDelta += SumDeltas(orphanMoves);
                    orphanMoves.Clear();
                    pressedButtons.Add(evt.MouseButton);
                    AddWithDelta(normalized, evt, evt.DeltaMs + pendingDelta);
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp when pressedButtons.Remove(evt.MouseButton):
                    AddWithDelta(normalized, evt, evt.DeltaMs + pendingDelta);
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp when orphanMoves.Count > 0:
                    AddRepairedDrag(normalized, orphanMoves, evt, pendingDelta);
                    orphanMoves.Clear();
                    pendingDelta = 0;
                    break;

                case InputEventType.MouseUp:
                    pendingDelta += evt.DeltaMs;
                    break;

                case InputEventType.MouseMove when pressedButtons.Count == 0:
                    orphanMoves.Add(evt);
                    break;

                case InputEventType.MouseMove:
                    pendingDelta += SumDeltas(orphanMoves);
                    orphanMoves.Clear();
                    AddWithDelta(normalized, evt, evt.DeltaMs + pendingDelta);
                    pendingDelta = 0;
                    break;

                default:
                    pendingDelta += SumDeltas(orphanMoves);
                    orphanMoves.Clear();
                    AddWithDelta(normalized, evt, evt.DeltaMs + pendingDelta);
                    pendingDelta = 0;
                    break;
            }
        }

        if (normalized.Count > 0)
            normalized[0].DeltaMs = 0;

        return normalized;
    }

    private static void AddRepairedDrag(
        ICollection<InputEvent> target,
        IReadOnlyList<InputEvent> orphanMoves,
        InputEvent mouseUp,
        long pendingDelta)
    {
        var firstMove = orphanMoves[0];
        target.Add(CreateSyntheticMouseDown(firstMove, mouseUp.MouseButton, firstMove.DeltaMs + pendingDelta));

        for (var i = 0; i < orphanMoves.Count; i++)
        {
            var move = orphanMoves[i];
            AddWithDelta(target, move, i == 0 ? 0 : move.DeltaMs);
        }

        target.Add(mouseUp);
    }

    private static InputEvent CreateSyntheticMouseDown(InputEvent source, int mouseButton, long deltaMs)
    {
        return new InputEvent
        {
            EventType = InputEventType.MouseDown,
            MouseButton = mouseButton,
            X = source.X,
            Y = source.Y,
            DeltaMs = deltaMs,
            UseWindowRelativeCoordinates = source.UseWindowRelativeCoordinates,
            RelativeX = source.RelativeX,
            RelativeY = source.RelativeY,
            RecordedWindowWidth = source.RecordedWindowWidth,
            RecordedWindowHeight = source.RecordedWindowHeight,
            UseWindowClientCoordinates = source.UseWindowClientCoordinates,
            ClientRelativeX = source.ClientRelativeX,
            ClientRelativeY = source.ClientRelativeY,
            RecordedClientWidth = source.RecordedClientWidth,
            RecordedClientHeight = source.RecordedClientHeight,
            WindowTitle = source.WindowTitle,
            WindowProcessName = source.WindowProcessName
        };
    }

    private static void AddWithDelta(ICollection<InputEvent> target, InputEvent evt, long deltaMs)
    {
        evt.DeltaMs = deltaMs;
        target.Add(evt);
    }

    private static long SumDeltas(IEnumerable<InputEvent> events)
    {
        var total = 0L;
        foreach (var evt in events)
            total += evt.DeltaMs;

        return total;
    }

    private static bool IsOwnAppMouseEvent(InputEvent evt)
    {
        if (evt.EventType is not (InputEventType.MouseDown or InputEventType.MouseUp or
            InputEventType.MouseMove or InputEventType.MouseWheel))
            return false;

        var processName = evt.WindowProcessName ?? string.Empty;
        var title = evt.WindowTitle ?? string.Empty;
        return processName.Contains("全能脚本", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("全能脚本", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("片段录制", StringComparison.OrdinalIgnoreCase);
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
