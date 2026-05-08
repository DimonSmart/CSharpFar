using CSharpFar.App.Settings;

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
              "shell": { "executable": "powershell.exe", "argumentsFormat": "-Command {0}", "kind": "ps" },
              "panels": { "options": { "showHiddenAndSystemFiles": false } },
              "history": { "maxCommandHistoryItems": 50, "maxDirectoryHistoryItems": 25, "maxFileHistoryItems": 10 }
            }
            """;
        File.WriteAllText(Path.Combine(configDir, "settings.json"), json);

        var store = new JsonSettingsStore(configDir);

        Assert.False(store.Settings.Ui.ConfirmDelete);
        Assert.False(store.Settings.Panels.Options.ShowHiddenAndSystemFiles);
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
        store.Save();

        var store2 = new JsonSettingsStore(configDir);
        Assert.False(store2.Settings.Panels.Options.ShowHiddenAndSystemFiles);
        Assert.Equal(@"C:\Projects", store2.Settings.Panels.LeftStartDirectory);
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
    public void CorruptJsonFallsBackToDefaults()
    {
        string configDir = Path.Combine(_tempDir, "config4");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "settings.json"), "{ not valid json !!!}}}");

        var store = new JsonSettingsStore(configDir);

        Assert.Equal("cmd.exe", store.Settings.Shell.Executable);
        Assert.True(store.Settings.Ui.ConfirmDelete);
    }
}
