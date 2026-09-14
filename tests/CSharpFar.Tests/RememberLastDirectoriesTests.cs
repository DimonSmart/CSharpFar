using CSharpFar.App.Bootstrap;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Settings;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class RememberLastDirectoriesTests : IDisposable
{
    private readonly string _tempDir;

    public RememberLastDirectoriesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarRememberLastDirectories_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Startup_WhenEnabled_RestoresEachRememberedDirectory()
    {
        var fs = new FakeFileSystemService();
        string leftStart = AddDirectory(fs, "left-start");
        string rightStart = AddDirectory(fs, "right-start");
        string leftRemembered = AddDirectory(fs, "left-remembered");
        string rightRemembered = AddDirectory(fs, "right-remembered");
        var settings = CreateSettings(leftStart, rightStart);
        settings.Panels.Options.RememberLastDirectories = true;
        settings.Panels.LastLeftDirectory = leftRemembered;
        settings.Panels.LastRightDirectory = rightRemembered;

        var session = CreateSession(settings, fs);

        Assert.Equal(leftRemembered, session.Panels.Left.CurrentDirectory);
        Assert.Equal(rightRemembered, session.Panels.Right.CurrentDirectory);
    }

    [Fact]
    public void Startup_WhenRememberedDirectoryIsUnavailable_FallsBackToConfiguredStartDirectory()
    {
        var fs = new FakeFileSystemService();
        string leftStart = AddDirectory(fs, "left-start");
        string rightStart = AddDirectory(fs, "right-start");
        var settings = CreateSettings(leftStart, rightStart);
        settings.Panels.Options.RememberLastDirectories = true;
        settings.Panels.LastLeftDirectory = Path.Combine(_tempDir, "missing-left");
        settings.Panels.LastRightDirectory = Path.Combine(_tempDir, "missing-right");

        var session = CreateSession(settings, fs);

        Assert.Equal(leftStart, session.Panels.Left.CurrentDirectory);
        Assert.Equal(rightStart, session.Panels.Right.CurrentDirectory);
    }

    [Fact]
    public void Startup_WhenDisabled_IgnoresRememberedDirectories()
    {
        var fs = new FakeFileSystemService();
        string leftStart = AddDirectory(fs, "left-start");
        string rightStart = AddDirectory(fs, "right-start");
        string leftRemembered = AddDirectory(fs, "left-remembered");
        string rightRemembered = AddDirectory(fs, "right-remembered");
        var settings = CreateSettings(leftStart, rightStart);
        settings.Panels.Options.RememberLastDirectories = false;
        settings.Panels.LastLeftDirectory = leftRemembered;
        settings.Panels.LastRightDirectory = rightRemembered;

        var session = CreateSession(settings, fs);

        Assert.Equal(leftStart, session.Panels.Left.CurrentDirectory);
        Assert.Equal(rightStart, session.Panels.Right.CurrentDirectory);
    }

    [Fact]
    public void NonLocalLocation_DoesNotReplaceLastLocalDirectory()
    {
        var state = new FilePanelState();
        string localDirectory = Path.Combine(_tempDir, "local");

        state.CurrentLocation = PanelLocation.Local(localDirectory);
        state.CurrentLocation = PanelLocation.SearchResult(localDirectory);

        Assert.Equal(PanelSourceId.SearchResults, state.SourceId);
        Assert.Equal(localDirectory, state.LastLocalDirectory);
    }

    [Fact]
    public void CaptureLastPanelDirectories_WhenEnabled_CopiesTransientStateToSettings()
    {
        var settings = new AppSettings();
        settings.Panels.Options.RememberLastDirectories = true;
        var left = new FilePanelState { LastLocalDirectory = "left-local" };
        var right = new FilePanelState { LastLocalDirectory = "right-local" };

        bool captured = ApplicationBootstrap.CaptureLastPanelDirectories(settings, left, right);

        Assert.True(captured);
        Assert.Equal("left-local", settings.Panels.LastLeftDirectory);
        Assert.Equal("right-local", settings.Panels.LastRightDirectory);
    }

    [Fact]
    public void CaptureLastPanelDirectories_WhenDisabled_LeavesPersistedValuesUnchanged()
    {
        var settings = new AppSettings();
        settings.Panels.LastLeftDirectory = "old-left";
        settings.Panels.LastRightDirectory = "old-right";
        var left = new FilePanelState { LastLocalDirectory = "new-left" };
        var right = new FilePanelState { LastLocalDirectory = "new-right" };

        bool captured = ApplicationBootstrap.CaptureLastPanelDirectories(settings, left, right);

        Assert.False(captured);
        Assert.Equal("old-left", settings.Panels.LastLeftDirectory);
        Assert.Equal("old-right", settings.Panels.LastRightDirectory);
    }

    [Fact]
    public void JsonSettingsStore_RoundTripsRememberedDirectories()
    {
        string configDirectory = Path.Combine(_tempDir, "config");
        var store = new JsonSettingsStore(configDirectory);
        store.Settings.Panels.Options.RememberLastDirectories = true;
        store.Settings.Panels.LastLeftDirectory = "left-local";
        store.Settings.Panels.LastRightDirectory = "right-local";
        store.Save();

        var reloaded = new JsonSettingsStore(configDirectory);

        Assert.True(reloaded.Settings.Panels.Options.RememberLastDirectories);
        Assert.Equal("left-local", reloaded.Settings.Panels.LastLeftDirectory);
        Assert.Equal("right-local", reloaded.Settings.Panels.LastRightDirectory);
    }

    [Fact]
    public void SettingsDialog_CanEnableRememberLastDirectories()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(Key(ConsoleKey.DownArrow));
        driver.EnqueueInput(Key(ConsoleKey.DownArrow));
        driver.EnqueueInput(Key(ConsoleKey.DownArrow));
        driver.EnqueueInput(Key(ConsoleKey.DownArrow));
        driver.EnqueueInput(Key(ConsoleKey.Enter));
        driver.EnqueueInput(Key(ConsoleKey.F10));

        SettingsDialogResult? result = new SettingsDialog(
            new DialogService(
                ModalTestHost.Create(driver),
                new FormFieldFactory(TextFieldHistoryTestProvider.Create())))
            .Show(
                PanelViewMode.Full,
                PanelViewMode.Full,
                "Default",
                fileHighlightingEnabled: true,
                editorSyntaxHighlightingEnabled: true,
                rememberLastDirectories: false);

        Assert.NotNull(result);
        Assert.True(result.RememberLastDirectories);
    }

    private string AddDirectory(FakeFileSystemService fs, string name)
    {
        string path = Path.Combine(_tempDir, name);
        fs.AddDirectory(path);
        return path;
    }

    private static AppSettings CreateSettings(string leftStart, string rightStart)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = leftStart;
        settings.Panels.RightStartDirectory = rightStart;
        return settings;
    }

    private static ApplicationSession CreateSession(AppSettings settings, FakeFileSystemService fs)
    {
        var controller = new PanelController(new FakePanelViewBuilder(fs));
        return ApplicationSessionFactory.Create(settings, controller, fs);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
}
