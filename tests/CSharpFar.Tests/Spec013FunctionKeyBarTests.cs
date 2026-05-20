using CSharpFar.App;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec013FunctionKeyBarTests : IDisposable
{
    private readonly string _root;

    public Spec013FunctionKeyBarTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec013_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_ModifierEventsSwitchFunctionKeyBarLayers()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        app.Run();

        var bottomWrites = driver.WriteRecords
            .Where(record => record.Y == 13)
            .Select(record => record.Text)
            .ToArray();

        Assert.Contains(bottomWrites, text => text.Contains("Search", StringComparison.Ordinal));
        Assert.Contains(bottomWrites, text => text.Contains("SortNm", StringComparison.Ordinal));
        Assert.Contains(bottomWrites, text => text.Contains("Help", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_ControlLayerShowsPanelVisibilityCommands()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        var app = CreateApp(fs, driver);

        SetFunctionKeyLayer(app, FunctionKeyLayer.Control);
        Render(app);

        string row = driver.GetRow(13);
        Assert.Contains("1LeftPn", row);
        Assert.Contains("2RightPn", row);
    }

    [Fact]
    public void Run_KeyEventWithAltModifierSwitchesFunctionKeyBarLayer()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.NoName, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        app.Run();

        var bottomWrites = driver.WriteRecords
            .Where(record => record.Y == 13)
            .Select(record => record.Text)
            .ToArray();

        Assert.Contains(bottomWrites, text => text.Contains("Search", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_ModifierOnlyEventsDoNotInsertCommandText()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        app.Run();

        Assert.Equal(string.Empty, GetCommandLine(app).Text);
    }

    [Fact]
    public void FunctionKeyLayerResolver_ShiftOnlyUsesShiftLayer()
    {
        Assert.Equal(
            FunctionKeyLayer.Shift,
            FunctionKeyLayerResolver.ResolvePressedLayer(ConsoleModifiers.Shift));

        Assert.True(FunctionKeyLayerResolver.TryResolveChordLayer(
            ConsoleModifiers.Shift,
            out var layer));
        Assert.Equal(FunctionKeyLayer.Shift, layer);
    }

    [Fact]
    public void FunctionKeyLayerResolver_CtrlAltUsesPlainLayer()
    {
        Assert.Equal(
            FunctionKeyLayer.Plain,
            FunctionKeyLayerResolver.ResolvePressedLayer(
                ConsoleModifiers.Alt | ConsoleModifiers.Control));

        Assert.False(FunctionKeyLayerResolver.TryResolveChordLayer(
            ConsoleModifiers.Alt | ConsoleModifiers.Control,
            out _));
    }

    [Fact]
    public void Run_SearchResultsPanelKeyBarHidesCapabilityBlockedCommands()
    {
        string foundFile = Path.Combine(_root, "found.txt");
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        SetSearchResultsPanel(GetLeftPanel(app), foundFile);

        app.Run();

        string row = ComposeBottomRow(driver, y: 13, width: 100);
        Assert.Contains("1Help", row);
        Assert.Contains("3View", row);
        Assert.Contains("5Copy", row);
        Assert.Contains("9ConfMn", row);
        Assert.DoesNotContain("4Edit", row);
        Assert.DoesNotContain("6RenMov", row);
        Assert.DoesNotContain("7MkFold", row);
        Assert.DoesNotContain("8Delete", row);
    }

    [Fact]
    public void Run_ControlFunctionKeyStillSortsActivePanel()
    {
        var fs = CreateFileSystem(
            new FilePanelItem
            {
                Name = "old.txt",
                FullPath = Path.Combine(_root, "old.txt"),
                IsDirectory = false,
                LastWriteTime = new DateTime(2020, 1, 1),
            },
            new FilePanelItem
            {
                Name = "new.txt",
                FullPath = Path.Combine(_root, "new.txt"),
                IsDirectory = false,
                LastWriteTime = new DateTime(2026, 1, 1),
            });
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.F5, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        app.Run();

        Assert.Equal(SortMode.LastWriteTime, GetLeftPanel(app).SortMode);
    }

    [Fact]
    public void Run_ClickPlainFunctionKeyBarF10Quits()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(LeftMouse(x: 73, y: 13, MouseEventKind.Down));

        var app = CreateApp(fs, driver);

        app.Run();

        string row = ComposeBottomRow(driver, y: 13, width: 100);
        Assert.Contains("10Quit", row);
    }

    [Fact]
    public void Run_ClickControlFunctionKeyBarSortsActivePanel()
    {
        var fs = CreateFileSystem(
            new FilePanelItem
            {
                Name = "b.txt",
                FullPath = Path.Combine(_root, "b.txt"),
                IsDirectory = false,
                Size = 2,
                LastWriteTime = new DateTime(2026, 1, 1),
            },
            new FilePanelItem
            {
                Name = "a.txt",
                FullPath = Path.Combine(_root, "a.txt"),
                IsDirectory = false,
                Size = 1,
                LastWriteTime = new DateTime(2026, 1, 1),
            });
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(LeftMouse(x: 33, y: 13, MouseEventKind.Down));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        SetFunctionKeyLayer(app, FunctionKeyLayer.Control);

        app.Run();

        Assert.Equal(SortMode.LastWriteTime, GetLeftPanel(app).SortMode);
    }

    private FakeFileSystemService CreateFileSystem(params FilePanelItem[] items)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, items);
        return fs;
    }

    private Application CreateApp(FakeFileSystemService fs, FakeConsoleDriver driver)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }

    private void SetSearchResultsPanel(FilePanelState state, string fullPath)
    {
        state.CurrentDirectory = _root;
        state.Items.Clear();
        state.Items.Add(new FilePanelItem
        {
            Name = Path.GetFileName(fullPath),
            FullPath = fullPath,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        });
        state.SelectedPaths.Clear();
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
        state.ProviderCapabilities = PanelProviderCapabilities.SearchResults;
        state.DisplayTitle = "Search results: *.txt";
        state.ShowCurrentItemFullPath = true;
        state.SearchRequest = new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.txt",
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 1,
        };
    }

    private static string ComposeBottomRow(FakeConsoleDriver driver, int y, int width)
    {
        var row = Enumerable.Repeat(' ', width).ToArray();
        foreach (var record in driver.WriteRecords.Where(record => record.Y == y))
        {
            for (int i = 0; i < record.Text.Length && record.X + i < width; i++)
                row[record.X + i] = record.Text[i];
        }

        return new string(row);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool control = false, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: control);

    private static MouseConsoleInputEvent LeftMouse(int x, int y, MouseEventKind kind) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static FilePanelState GetLeftPanel(Application app)
    {
        var field = typeof(Application).GetField(
            "_left",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._left field not found.");

        return (FilePanelState)field.GetValue(app)!;
    }

    private static CommandLineState GetCommandLine(Application app)
    {
        var field = typeof(Application).GetField(
            "_cmdLine",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");

        return (CommandLineState)field.GetValue(app)!;
    }

    private static void SetFunctionKeyLayer(Application app, FunctionKeyLayer layer)
    {
        var field = typeof(Application).GetField(
            "_functionKeyLayer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._functionKeyLayer field not found.");

        field.SetValue(app, layer);
    }

    private static void Render(Application app)
    {
        var method = typeof(Application).GetMethod(
            "Render",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.Render method not found.");

        method.Invoke(app, []);
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
    }

    private sealed class NoOpFileOperationService : IFileOperationService
    {
        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
    }
}
