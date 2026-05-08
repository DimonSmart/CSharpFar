using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarAppSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_AppliesDefaultSortModeFromSettings()
    {
        var app = CreateApp("Size",
            new FilePanelItem { Name = "large.txt", FullPath = Path.Combine(_tempDir, "large.txt"), IsDirectory = false, Size = 100 },
            new FilePanelItem { Name = "small.txt", FullPath = Path.Combine(_tempDir, "small.txt"), IsDirectory = false, Size = 1 });

        var left = GetLeftPanel(app);

        Assert.Equal(SortMode.Size, left.SortMode);
        Assert.Equal("small.txt", left.Items[1].Name);
        Assert.Equal("large.txt", left.Items[2].Name);
    }

    [Fact]
    public void Constructor_UsesNameSortWhenDefaultSortModeIsInvalid()
    {
        var app = CreateApp("not-a-sort-mode",
            new FilePanelItem { Name = "b.txt", FullPath = Path.Combine(_tempDir, "b.txt"), IsDirectory = false },
            new FilePanelItem { Name = "a.txt", FullPath = Path.Combine(_tempDir, "a.txt"), IsDirectory = false });

        var left = GetLeftPanel(app);

        Assert.Equal(SortMode.Name, left.SortMode);
        Assert.Equal("a.txt", left.Items[1].Name);
        Assert.Equal("b.txt", left.Items[2].Name);
    }

    [Fact]
    public void Run_CtrlSControlCharacter_OpensSettingsDialog()
    {
        var driver = new FakeConsoleDriver();
        int saveCount = 0;

        driver.EnqueueKey(new ConsoleKeyInfo('\u0013', ConsoleKey.NoName, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(
            "Name",
            driver,
            saveSettings: () => saveCount++);

        app.Run();

        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void Run_F9_OpensSettingsDialog()
    {
        var driver = new FakeConsoleDriver();
        int saveCount = 0;

        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(
            "Name",
            driver,
            saveSettings: () => saveCount++);

        app.Run();

        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void Run_F10WithUnexpectedPrintableChar_StillQuits()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('D', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp("Name", driver);

        app.Run();

        var cmdLine = typeof(Application).GetField("_cmdLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");
        var state = (CommandLineState)cmdLine.GetValue(app)!;
        Assert.Equal(string.Empty, state.Text);
    }

    [Fact]
    public void Run_DownArrowWithUnexpectedPrintableChar_DoesNotType()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('B', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp("Name", driver);

        app.Run();

        var cmdLine = typeof(Application).GetField("_cmdLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");
        var state = (CommandLineState)cmdLine.GetValue(app)!;
        Assert.Equal(string.Empty, state.Text);
    }

    [Fact]
    public void RenderClock_UsesActivePathColors()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 5);
        var app = CreateApp("Name", driver);

        var method = typeof(Application).GetMethod(
            "RenderClock",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.RenderClock method not found.");

        method.Invoke(app, [new ConsoleSize(20, 5)]);

        for (int x = 0; x < 20; x++)
        {
            var cell = driver.GetCell(x, 0);
            if (cell.Character == ' ')
                continue;

            Assert.Equal(PaletteRegistry.Default.PanelPathActiveFg, cell.Foreground);
            Assert.Equal(PaletteRegistry.Default.PanelPathActiveBg, cell.Background);
        }
    }

    [Fact]
    public void Render_UsesJoinedCenterPanelFrame()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp("Name", driver);

        var method = typeof(Application).GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.Render method not found.");

        method.Invoke(app, []);

        Assert.Equal('╦', driver.GetCell(39, 0).Character);
        Assert.Equal('╫', driver.GetCell(39, 6).Character);
        Assert.Equal('╩', driver.GetCell(39, 9).Character);
    }

    private Application CreateApp(string sortMode, params FilePanelItem[] items)
    {
        return CreateApp(sortMode, new FakeConsoleDriver(), saveSettings: null, items);
    }

    private Application CreateApp(
        string sortMode,
        FakeConsoleDriver driver,
        Action? saveSettings = null,
        params FilePanelItem[] items)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir, items);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;
        settings.Panels.DefaultSortMode = sortMode;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            saveSettings: saveSettings);
    }

    private static FilePanelState GetLeftPanel(Application app)
    {
        var field = typeof(Application).GetField("_left", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._left field not found.");
        return (FilePanelState)field.GetValue(app)!;
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
    }

    private sealed class NoOpFileOperationService : IFileOperationService
    {
        public Task CopyAsync(
            IReadOnlyList<string> sources,
            string destination,
            Action<string>? onProgress = null,
            Func<string, ConflictChoice>? onConflict = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MoveAsync(
            IReadOnlyList<string> sources,
            string destination,
            Func<string, ConflictChoice>? onConflict = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void CreateDirectory(string path) { }
    }
}
