using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PanelSettingsSaveCancelTests : IDisposable
{
    private readonly string _tempDir;

    public PanelSettingsSaveCancelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarPanelSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Settings_MultiGroupChangesApplyOnlyOnSave(bool save)
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\u0013', ConsoleKey.NoName, shift: false, alt: false, control: true));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(save ? ConsoleKey.F10 : ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;
        settings.Panels.LeftViewMode = PanelViewMode.Full.ToString();
        settings.Panels.RightViewMode = PanelViewMode.Full.ToString();
        settings.Panels.Options.SelectFolders = true;
        settings.Panels.Options.ShowParentDirectoryInRootFolders = false;

        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        int saveCount = 0;
        var app = new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            saveSettings: () => saveCount++);
        int readsBeforeRun = fs.ReadDirectoryCallCount;

        app.Run();

        if (save)
        {
            Assert.Equal(PanelViewMode.BriefTwoColumns, app.Session.Panels.LeftViewMode);
            Assert.Equal(PanelViewMode.BriefTwoColumns.ToString(), settings.Panels.LeftViewMode);
            Assert.False(settings.Panels.Options.SelectFolders);
            Assert.True(settings.Panels.Options.ShowParentDirectoryInRootFolders);
            Assert.Equal(1, saveCount);
            Assert.Equal(readsBeforeRun + 2, fs.ReadDirectoryCallCount);
        }
        else
        {
            Assert.Equal(PanelViewMode.Full, app.Session.Panels.LeftViewMode);
            Assert.Equal(PanelViewMode.Full.ToString(), settings.Panels.LeftViewMode);
            Assert.True(settings.Panels.Options.SelectFolders);
            Assert.False(settings.Panels.Options.ShowParentDirectoryInRootFolders);
            Assert.Equal(0, saveCount);
            Assert.Equal(readsBeforeRun, fs.ReadDirectoryCallCount);
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
