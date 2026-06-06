using System.Collections.ObjectModel;
using System.IO;
using AutoMacro.Models;
using AutoMacro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AutoMacro.ViewModels;

public partial class ActionListViewModel : ObservableObject
{
    private readonly IProfileManager _profileManager;
    private RecordProfile? _currentProfile;

    [ObservableProperty]
    private ObservableCollection<InputEvent> _actions = new();

    [ObservableProperty]
    private InputEvent? _selectedAction;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public ActionListViewModel(IProfileManager profileManager)
    {
        _profileManager = profileManager;
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
    private void AddImageClick()
    {
        var path = PickImage();
        if (path is null) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.ImageClick,
            ImagePath = CopyImageToProfile(path),
            TimeoutMs = 5000,
            MatchThreshold = 0.92,
            DeltaMs = 100
        });
    }

    [RelayCommand]
    private void AddWaitImage()
    {
        var path = PickImage();
        if (path is null) return;

        InsertAction(new InputEvent
        {
            EventType = InputEventType.WaitImage,
            ImagePath = CopyImageToProfile(path),
            TimeoutMs = 8000,
            MatchThreshold = 0.92,
            DeltaMs = 0
        });
    }

    [RelayCommand]
    private void AddWaitWindow()
    {
        var title = PromptText("等待窗口", "输入窗口标题关键字：");
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
        var title = PromptText("激活窗口", "输入窗口标题关键字：");
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

    [RelayCommand]
    private void ClearAll()
    {
        if (Actions.Count == 0) return;

        var result = System.Windows.MessageBox.Show(
            "确定要清空所有动作记录吗？\n清空后可重新录制。",
            "清空动作",
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
        if (_currentProfile is not null)
            _profileManager.SaveActions(_currentProfile, Actions.ToList());
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
            Title = "选择模板图片",
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

    private static string PromptText(string title, string prompt)
    {
        return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, "");
    }
}
