using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AutoMacro.Models;

namespace AutoMacro.Services;

public class ProfileManager : IProfileManager
{
    private const string ProfilesFolderName = "Profiles";
    private const string HotkeySettingsFileName = "hotkey_settings.json";
    private const string GlobalConfigManifestFileName = "global_config.json";

    private sealed class ProfileMetadata
    {
        public int LoopCount { get; set; } = 1;
        public double PlaybackSpeed { get; set; } = 1.0;
    }

    private sealed class GlobalConfigManifest
    {
        public string PackageType { get; set; } = "AutoMacroGlobalConfig";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _profilesRoot;

    public ObservableCollection<RecordProfile> Profiles { get; } = new();

    public ProfileManager()
    {
        _profilesRoot = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, ProfilesFolderName);
        if (!Directory.Exists(_profilesRoot))
            Directory.CreateDirectory(_profilesRoot);
    }

    public void LoadAllProfiles()
    {
        Profiles.Clear();
        if (!Directory.Exists(_profilesRoot)) return;

        foreach (var dir in Directory.GetDirectories(_profilesRoot))
        {
            var profile = new RecordProfile
            {
                Name = Path.GetFileName(dir),
                FolderPath = dir,
                CreatedAt = Directory.GetCreationTime(dir)
            };

            LoadProfileMetadata(profile);
            Profiles.Add(profile);
        }
    }

    public RecordProfile CreateProfile(string name)
    {
        name = NormalizeProfileName(name);
        var folderPath = Path.Combine(_profilesRoot, name);
        if (Directory.Exists(folderPath))
            throw new InvalidOperationException($"方案 \"{name}\" 已存在。");

        Directory.CreateDirectory(folderPath);

        var profile = new RecordProfile
        {
            Name = name,
            FolderPath = folderPath,
            CreatedAt = DateTime.Now
        };

        // Create empty files
        File.WriteAllText(profile.RecordFilePath, "[]");
        File.WriteAllText(profile.VariableFilePath, string.Empty);
        SaveProfile(profile);

        Profiles.Add(profile);
        return profile;
    }

    public void DeleteProfile(RecordProfile profile)
    {
        if (Directory.Exists(profile.FolderPath))
            Directory.Delete(profile.FolderPath, true);
        Profiles.Remove(profile);
    }

    public RecordProfile RenameProfile(RecordProfile profile, string newName)
    {
        newName = NormalizeProfileName(newName);
        var newFolderPath = Path.Combine(_profilesRoot, newName);
        if (Directory.Exists(newFolderPath))
            throw new InvalidOperationException($"方案 \"{newName}\" 已存在");

        Directory.Move(profile.FolderPath, newFolderPath);

        var idx = Profiles.IndexOf(profile);
        var renamed = new RecordProfile
        {
            Name = newName,
            FolderPath = newFolderPath,
            CreatedAt = profile.CreatedAt,
            LoopCount = profile.LoopCount,
            PlaybackSpeed = profile.PlaybackSpeed
        };

        SaveProfile(renamed);

        if (idx >= 0)
            Profiles[idx] = renamed;

        return renamed;
    }

    public void SaveProfile(RecordProfile profile)
    {
        Directory.CreateDirectory(profile.FolderPath);

        var metadata = new ProfileMetadata
        {
            LoopCount = Math.Max(0, profile.LoopCount),
            PlaybackSpeed = NormalizePlaybackSpeed(profile.PlaybackSpeed)
        };

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(profile.ProfileFilePath, json);
    }

    public List<InputEvent> LoadActions(RecordProfile profile)
    {
        if (!File.Exists(profile.RecordFilePath))
            return new List<InputEvent>();

        var json = File.ReadAllText(profile.RecordFilePath);
        var events = JsonSerializer.Deserialize<List<InputEvent>>(json, JsonOptions)
                     ?? new List<InputEvent>();
        NormalizeLoadedAssetPaths(profile, events);
        return events;
    }

    public void SaveActions(RecordProfile profile, IList<InputEvent> events)
    {
        var json = JsonSerializer.Serialize(events.Select(e => CloneForSave(profile, e)).ToList(), JsonOptions);
        File.WriteAllText(profile.RecordFilePath, json);
    }

    public VariableTable LoadVariableTable(RecordProfile profile)
    {
        return VariableTable.LoadFromCsv(profile.VariableFilePath);
    }

    public void SaveVariableTable(RecordProfile profile, VariableTable table)
    {
        table.SaveToCsv(profile.VariableFilePath);
    }

    public void ExportProfile(RecordProfile profile, string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(profile.FolderPath, zipPath, CompressionLevel.Optimal, true);
    }

    public RecordProfile ImportProfile(string zipPath)
    {
        // 解压到临时目录先看看里面的文件夹名
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        ZipFile.ExtractToDirectory(zipPath, tempDir);

        // zip 里应该有一个子文件夹（方案名）
        var subDirs = Directory.GetDirectories(tempDir);
        string sourceFolderPath;
        string profileName;

        if (subDirs.Length > 0)
        {
            sourceFolderPath = subDirs[0];
            profileName = Path.GetFileName(sourceFolderPath);
        }
        else
        {
            // 没有子文件夹，文件直接在根目录，用 zip 文件名作为方案名
            profileName = Path.GetFileNameWithoutExtension(zipPath);
            sourceFolderPath = tempDir;
        }

        // 如果方案名已存在，自动加后缀
        var targetPath = Path.Combine(_profilesRoot, profileName);
        var originalName = profileName;
        var counter = 1;
        while (Directory.Exists(targetPath))
        {
            profileName = $"{originalName}_{counter}";
            targetPath = Path.Combine(_profilesRoot, profileName);
            counter++;
        }

        // 复制到 Profiles 目录
        CopyDirectory(sourceFolderPath, targetPath);

        // 清理临时目录
        try { Directory.Delete(tempDir, true); } catch { }

        var profile = new RecordProfile
        {
            Name = profileName,
            FolderPath = targetPath,
            CreatedAt = DateTime.Now
        };

        LoadProfileMetadata(profile);
        SaveProfile(profile);
        Profiles.Add(profile);
        return profile;
    }

    public void ExportGlobalConfig(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var parent = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        if (!File.Exists(HotkeySettings.SettingsFilePath))
            new HotkeySettings().Save();

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tempDir);

            var manifest = JsonSerializer.Serialize(new GlobalConfigManifest(), JsonOptions);
            File.WriteAllText(Path.Combine(tempDir, GlobalConfigManifestFileName), manifest);

            var tempProfilesRoot = Path.Combine(tempDir, ProfilesFolderName);
            if (Directory.Exists(_profilesRoot))
            {
                CopyDirectory(_profilesRoot, tempProfilesRoot, overwrite: true);
                BundleReferencedImagesForExport(_profilesRoot, tempProfilesRoot);
            }
            else
            {
                Directory.CreateDirectory(tempProfilesRoot);
            }

            File.Copy(
                HotkeySettings.SettingsFilePath,
                Path.Combine(tempDir, HotkeySettingsFileName),
                overwrite: true);

            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public void ImportGlobalConfig(string zipPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            var root = FindGlobalConfigRoot(tempDir);
            var sourceProfilesRoot = Path.Combine(root, ProfilesFolderName);
            var sourceHotkeySettings = Path.Combine(root, HotkeySettingsFileName);

            if (!Directory.Exists(sourceProfilesRoot))
                throw new InvalidOperationException("这个压缩包里没有找到 Profiles 文件夹，不是完整的全局配置包。");

            var backupProfilesRoot = Path.Combine(tempDir, "current_Profiles_backup");
            var backupHotkeySettings = Path.Combine(tempDir, "current_hotkey_settings_backup.json");

            if (Directory.Exists(_profilesRoot))
                Directory.Move(_profilesRoot, backupProfilesRoot);

            if (File.Exists(HotkeySettings.SettingsFilePath))
                File.Copy(HotkeySettings.SettingsFilePath, backupHotkeySettings, overwrite: true);

            try
            {
                CopyDirectory(sourceProfilesRoot, _profilesRoot, overwrite: true);

                if (File.Exists(sourceHotkeySettings))
                {
                    File.Copy(
                        sourceHotkeySettings,
                        HotkeySettings.SettingsFilePath,
                        overwrite: true);
                }
            }
            catch
            {
                if (Directory.Exists(_profilesRoot))
                    Directory.Delete(_profilesRoot, true);

                if (Directory.Exists(backupProfilesRoot))
                    Directory.Move(backupProfilesRoot, _profilesRoot);

                if (File.Exists(backupHotkeySettings))
                    File.Copy(backupHotkeySettings, HotkeySettings.SettingsFilePath, overwrite: true);

                throw;
            }

            LoadAllProfiles();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private void LoadProfileMetadata(RecordProfile profile)
    {
        profile.LoopCount = 1;
        profile.PlaybackSpeed = 1.0;

        if (!File.Exists(profile.ProfileFilePath))
            return;

        try
        {
            var json = File.ReadAllText(profile.ProfileFilePath);
            var metadata = JsonSerializer.Deserialize<ProfileMetadata>(json, JsonOptions);
            profile.LoopCount = Math.Max(0, metadata?.LoopCount ?? 1);
            profile.PlaybackSpeed = NormalizePlaybackSpeed(metadata?.PlaybackSpeed ?? 1.0);
        }
        catch
        {
            profile.LoopCount = 1;
            profile.PlaybackSpeed = 1.0;
        }
    }

    private static string NormalizeProfileName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("方案名称不能为空。");

        if (trimmed is "." or ".." || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("方案名称不能包含文件名非法字符。");

        return trimmed;
    }

    private static double NormalizePlaybackSpeed(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 1.0;

        return Math.Clamp(value, 0.1, 10.0);
    }

    private static void NormalizeLoadedAssetPaths(RecordProfile profile, IEnumerable<InputEvent> events)
    {
        foreach (var evt in events)
        {
            if (!string.IsNullOrWhiteSpace(evt.ImagePath) && !Path.IsPathRooted(evt.ImagePath))
                evt.ImagePath = Path.Combine(profile.FolderPath, evt.ImagePath);
        }
    }

    private static InputEvent CloneForSave(RecordProfile profile, InputEvent evt)
    {
        return new InputEvent
        {
            DeltaMs = evt.DeltaMs,
            EventType = evt.EventType,
            KeyCode = evt.KeyCode,
            MouseButton = evt.MouseButton,
            X = evt.X,
            Y = evt.Y,
            WheelDelta = evt.WheelDelta,
            VariableMarker = evt.VariableMarker,
            UseWindowRelativeCoordinates = evt.UseWindowRelativeCoordinates,
            RelativeX = evt.RelativeX,
            RelativeY = evt.RelativeY,
            RecordedWindowWidth = evt.RecordedWindowWidth,
            RecordedWindowHeight = evt.RecordedWindowHeight,
            UseWindowClientCoordinates = evt.UseWindowClientCoordinates,
            ClientRelativeX = evt.ClientRelativeX,
            ClientRelativeY = evt.ClientRelativeY,
            RecordedClientWidth = evt.RecordedClientWidth,
            RecordedClientHeight = evt.RecordedClientHeight,
            WindowTitle = evt.WindowTitle,
            WindowProcessName = evt.WindowProcessName,
            ImagePath = ToProfileRelativePath(profile, evt.ImagePath),
            MatchThreshold = evt.MatchThreshold,
            TimeoutMs = evt.TimeoutMs,
            AfterFoundDelayMs = evt.AfterFoundDelayMs,
            TextPattern = evt.TextPattern,
            UseOcrRegion = evt.UseOcrRegion,
            OcrRegionX = evt.OcrRegionX,
            OcrRegionY = evt.OcrRegionY,
            OcrRegionWidth = evt.OcrRegionWidth,
            OcrRegionHeight = evt.OcrRegionHeight,
            RequestUrl = evt.RequestUrl,
            RequestMethod = evt.RequestMethod,
            RequestBody = evt.RequestBody,
            ResponseVariableName = evt.ResponseVariableName
        };
    }

    private static string? ToProfileRelativePath(RecordProfile profile, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return path;

        var relative = Path.GetRelativePath(profile.FolderPath, path);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? path
            : relative;
    }

    private static string FindGlobalConfigRoot(string tempDir)
    {
        if (ContainsGlobalConfigFiles(tempDir))
            return tempDir;

        var subDirs = Directory.GetDirectories(tempDir);
        if (subDirs.Length == 1 && ContainsGlobalConfigFiles(subDirs[0]))
            return subDirs[0];

        throw new InvalidOperationException("这个压缩包不是全局配置包。请确认它是从“导出全局配置”生成的。");
    }

    private static bool ContainsGlobalConfigFiles(string dir)
    {
        return Directory.Exists(Path.Combine(dir, ProfilesFolderName)) ||
               File.Exists(Path.Combine(dir, HotkeySettingsFileName)) ||
               File.Exists(Path.Combine(dir, GlobalConfigManifestFileName));
    }

    private static void BundleReferencedImagesForExport(string sourceProfilesRoot, string exportProfilesRoot)
    {
        foreach (var sourceProfileDir in Directory.GetDirectories(sourceProfilesRoot))
        {
            var profileName = Path.GetFileName(sourceProfileDir);
            var exportProfileDir = Path.Combine(exportProfilesRoot, profileName);
            BundleReferencedImagesForProfile(sourceProfileDir, exportProfileDir);
        }
    }

    private static void BundleReferencedImagesForProfile(string sourceProfileDir, string exportProfileDir)
    {
        var recordPath = Path.Combine(exportProfileDir, "record.json");
        if (!File.Exists(recordPath))
            return;

        try
        {
            var json = File.ReadAllText(recordPath);
            var events = JsonSerializer.Deserialize<List<InputEvent>>(json, JsonOptions);
            if (events is null || events.Count == 0)
                return;

            var copiedExternalImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            foreach (var evt in events)
            {
                var bundledPath = BundleImageForExport(
                    evt.ImagePath,
                    sourceProfileDir,
                    exportProfileDir,
                    copiedExternalImages);

                if (bundledPath is null || evt.ImagePath == bundledPath)
                    continue;

                evt.ImagePath = bundledPath;
                changed = true;
            }

            if (!changed)
                return;

            File.WriteAllText(recordPath, JsonSerializer.Serialize(events, JsonOptions));
        }
        catch
        {
            // 导出不能因为某个旧 record.json 格式异常就中断；原文件仍会随方案一起打包。
        }
    }

    private static string? BundleImageForExport(
        string? imagePath,
        string sourceProfileDir,
        string exportProfileDir,
        Dictionary<string, string> copiedExternalImages)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var sourceImagePath = Path.IsPathRooted(imagePath)
            ? imagePath
            : Path.GetFullPath(Path.Combine(sourceProfileDir, imagePath));

        if (!File.Exists(sourceImagePath))
            return null;

        var sourceImageFullPath = Path.GetFullPath(sourceImagePath);

        if (IsInsideDirectory(sourceProfileDir, sourceImageFullPath))
        {
            var relativePath = Path.GetRelativePath(sourceProfileDir, sourceImageFullPath);
            var exportedImagePath = Path.Combine(exportProfileDir, relativePath);

            if (!File.Exists(exportedImagePath))
            {
                var exportedImageDir = Path.GetDirectoryName(exportedImagePath);
                if (!string.IsNullOrWhiteSpace(exportedImageDir))
                    Directory.CreateDirectory(exportedImageDir);

                File.Copy(sourceImageFullPath, exportedImagePath, overwrite: true);
            }

            return relativePath;
        }

        if (copiedExternalImages.TryGetValue(sourceImageFullPath, out var existingRelativePath))
            return existingRelativePath;

        var imagesDir = Path.Combine(exportProfileDir, "Images");
        Directory.CreateDirectory(imagesDir);

        var destinationPath = GetUniqueDestinationPath(imagesDir, Path.GetFileName(sourceImageFullPath));
        File.Copy(sourceImageFullPath, destinationPath, overwrite: true);

        var relative = Path.GetRelativePath(exportProfileDir, destinationPath);
        copiedExternalImages[sourceImageFullPath] = relative;
        return relative;
    }

    private static bool IsInsideDirectory(string directory, string filePath)
    {
        var directoryFullPath = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fileFullPath = Path.GetFullPath(filePath);

        return fileFullPath.StartsWith(directoryFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDestinationPath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"{Guid.NewGuid():N}.png";

        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination))
            return destination;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    private static void CopyDirectory(string source, string destination, bool overwrite = false)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), overwrite);
    }
}
