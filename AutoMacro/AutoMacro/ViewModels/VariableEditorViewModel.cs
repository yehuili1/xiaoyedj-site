using System.Collections.ObjectModel;
using AutoMacro.Models;
using AutoMacro.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoMacro.ViewModels;

public partial class VariableEditorViewModel : ObservableObject
{
    private readonly IProfileManager _profileManager;
    private RecordProfile? _currentProfile;

    public VariableTable VariableTable { get; private set; } = new();

    [ObservableProperty]
    private ObservableCollection<VariableItem> _items = new();

    public VariableEditorViewModel(IProfileManager profileManager)
    {
        _profileManager = profileManager;
    }

    public void LoadFromProfile(RecordProfile profile)
    {
        _currentProfile = profile;
        VariableTable = _profileManager.LoadVariableTable(profile);
        SyncFromTable();
    }

    private void SyncFromTable()
    {
        Items.Clear();
        foreach (var value in VariableTable.GetAllValues())
            Items.Add(new VariableItem { Value = value });
    }

    private void SyncToTable()
    {
        VariableTable.SetAllValues(Items.Select(i => i.Value).ToList());
    }

    public void SyncPendingChanges()
    {
        SyncToTable();
    }

    public void AddRow()
    {
        Items.Add(new VariableItem { Value = string.Empty });
        SyncToTable();
    }

    public void RemoveRow(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            Items.RemoveAt(index);
            SyncToTable();
        }
    }

    public void ClearAll()
    {
        Items.Clear();
        VariableTable.Clear();
    }

    public void SaveCsv()
    {
        SyncPendingChanges();
        if (_currentProfile is not null)
            _profileManager.SaveVariableTable(_currentProfile, VariableTable);
    }
}

public partial class VariableItem : ObservableObject
{
    [ObservableProperty]
    private string _value = string.Empty;
}
