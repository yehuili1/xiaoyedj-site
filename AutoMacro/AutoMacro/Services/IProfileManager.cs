using System.Collections.ObjectModel;
using AutoMacro.Models;

namespace AutoMacro.Services;

public interface IProfileManager
{
    ObservableCollection<RecordProfile> Profiles { get; }
    void LoadAllProfiles();
    RecordProfile CreateProfile(string name);
    void DeleteProfile(RecordProfile profile);
    RecordProfile RenameProfile(RecordProfile profile, string newName);
    void SaveProfile(RecordProfile profile);
    List<InputEvent> LoadActions(RecordProfile profile);
    void SaveActions(RecordProfile profile, IList<InputEvent> events);
    VariableTable LoadVariableTable(RecordProfile profile);
    void SaveVariableTable(RecordProfile profile, VariableTable table);
    void ExportProfile(RecordProfile profile, string zipPath);
    RecordProfile ImportProfile(string zipPath);
}
