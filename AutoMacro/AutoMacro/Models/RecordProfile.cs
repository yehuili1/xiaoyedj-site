using System.IO;

namespace AutoMacro.Models;

public class RecordProfile
{
    public string Name { get; set; } = "Untitled";
    public string FolderPath { get; set; } = string.Empty;
    public string RecordFilePath => Path.Combine(FolderPath, "record.json");
    public string VariableFilePath => Path.Combine(FolderPath, "variables.csv");
    public string ProfileFilePath => Path.Combine(FolderPath, "profile.json");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int LoopCount { get; set; } = 1;
    public double PlaybackSpeed { get; set; } = 1.0;

    public override string ToString() => Name;
}
