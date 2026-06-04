using CSharpFar.App.Settings;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 15: JsonSettingsStore creates, loads, and saves settings.json;
/// supports portable mode.
/// </summary>
public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSetTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CreatesDefaultSettingsWhenFileDoesNotExist()
    {
        string configDir = Path.Combine(_tempDir, "config1");
        var store = new JsonSettingsStore(configDir);

        Assert.True(File.Exists(Path.Combine(configDir, "settings.json")));
        Assert.True(store.Settings.Ui.ConfirmDelete);
        Assert.Equal("cmd.exe", store.Settings.Shell.Executable);
        Assert.True(store.Settings.FileOperations.ShowTotalProgress);
        Assert.Equal("Inherit", store.Settings.FileOperations.SecurityMode);
        Assert.Equal(1000, store.Settings.History.MaxCommandHistoryItems);
    }

    [Fact]
    public void LoadsExistingSettings()
    {
        string configDir = Path.Combine(_tempDir, "config2");
        Directory.CreateDirectory(configDir);

        string json = """
            {
              "ui": { "confirmDelete": false },
              "shell": { "executable": "powershell.exe", "argumentsFormat": "-Command {0}" },
              "panels": { "options": { "showHiddenAndSystemFiles": false } },
              "fileOperations": { "useRecycleBinForDelete": false, "conflictDecision": "OnlyNewer" },
              "history": { "maxCommandHistoryItems": 50, "maxDirectoryHistoryItems": 25, "maxFileHistoryItems": 10 }
            }
            """;
        File.WriteAllText(Path.Combine(configDir, "settings.json"), json);

        var store = new JsonSettingsStore(configDir);

        Assert.False(store.Settings.Ui.ConfirmDelete);
        Assert.False(store.Settings.Panels.Options.ShowHiddenAndSystemFiles);
        Assert.False(store.Settings.FileOperations.UseRecycleBinForDelete);
        Assert.Equal("OnlyNewer", store.Settings.FileOperations.ConflictDecision);
        Assert.Equal("powershell.exe", store.Settings.Shell.Executable);
        Assert.Equal(50, store.Settings.History.MaxCommandHistoryItems);
        Assert.Equal(10, store.Settings.History.MaxFileHistoryItems);
    }

    [Fact]
    public void SaveAndReload_PreservesModifiedValues()
    {
        string configDir = Path.Combine(_tempDir, "config3");
        var store = new JsonSettingsStore(configDir);

        store.Settings.Panels.Options.ShowHiddenAndSystemFiles = false;
        store.Settings.Panels.LeftStartDirectory = @"C:\Projects";
        store.Settings.FileOperations.PreserveAttributes = false;
        store.Save();

        var store2 = new JsonSettingsStore(configDir);
        Assert.False(store2.Settings.Panels.Options.ShowHiddenAndSystemFiles);
        Assert.Equal(@"C:\Projects", store2.Settings.Panels.LeftStartDirectory);
        Assert.False(store2.Settings.FileOperations.PreserveAttributes);
    }

    [Fact]
    public void SaveAndReload_PreservesDirectoryShortcuts()
    {
        string configDir = Path.Combine(_tempDir, "directory-shortcuts");
        var store = new JsonSettingsStore(configDir);
        store.Settings.DirectoryShortcuts.Items.Add(new AppSettings.DirectoryShortcutItem
        {
            Number = 4,
            Name = "Work",
            Path = @"C:\Work",
        });

        store.Save();

        var store2 = new JsonSettingsStore(configDir);
        var item = Assert.Single(store2.Settings.DirectoryShortcuts.Items);
        Assert.Equal(4, item.Number);
        Assert.Equal("Work", item.Name);
        Assert.Equal(@"C:\Work", item.Path);
    }

    [Fact]
    public void NonPortableModeUsesAppDataDirectory()
    {
        string exeDir = Path.Combine(_tempDir, "exedir");
        Directory.CreateDirectory(exeDir);
        // No CSharpFar.portable file

        string result = JsonSettingsStore.ResolveConfigDirectory(exeDir);

        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PortableModeUsesExeDirectory()
    {
        string exeDir = Path.Combine(_tempDir, "exedir2");
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, "CSharpFar.portable"), "");

        string result = JsonSettingsStore.ResolveConfigDirectory(exeDir);

        Assert.Equal(Path.Combine(exeDir, "CSharpFar.config"), result);
    }

    [Fact]
    public void CorruptJsonThrows()
    {
        string configDir = Path.Combine(_tempDir, "config4");
        Directory.CreateDirectory(configDir);
        string filePath = Path.Combine(configDir, "settings.json");
        File.WriteAllText(filePath, "{ not valid json !!!}}}");

        var ex = Assert.Throws<InvalidDataException>(() => new JsonSettingsStore(configDir));

        Assert.Contains(filePath, ex.Message, StringComparison.Ordinal);
    }
}
