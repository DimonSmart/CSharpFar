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
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Shift));
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
        Assert.Contains(bottomWrites, text => text.Contains("New", StringComparison.Ordinal));
        Assert.Contains(bottomWrites, text => text.Contains("Help", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_KeyChordAloneDoesNotSwitchFunctionKeyBarLayer()
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

        Assert.DoesNotContain(bottomWrites, text => text.Contains("Search", StringComparison.Ordinal));
        Assert.Contains(bottomWrites, text => text.Contains("Help", StringComparison.Ordinal));
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


    [Theory]
    [InlineData(ConsoleModifiers.Control)]
    [InlineData(ConsoleModifiers.Alt)]
    [InlineData(ConsoleModifiers.Shift)]
    public void Run_CtrlOResetsHeldLayerEvenWithoutReleaseEvent(ConsoleModifiers heldModifier)
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(heldModifier));
        driver.EnqueueKey(CtrlO());
        driver.EnqueueKey(CtrlO());
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        int read = 0;
        driver.BeforeReadInput = _ =>
        {
            switch (read++)
            {
                case 1:
                    Assert.Equal(FunctionKeyLayerResolver.ResolvePressedLayer(heldModifier), app.Session.FunctionKeyLayer);
                    break;
                case 2:
                    Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, app.Session.App.WorkspaceMode);
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    break;
                case 3:
                    Assert.Equal(ApplicationWorkspaceMode.Panels, app.Session.App.WorkspaceMode);
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    Assert.Contains("1Help", driver.GetRow(13));
                    break;
            }
        };

        app.Run();
        Assert.Equal(4, read);
        Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
    }

    [Fact]
    public void Run_ReleaseInHiddenWorkspaceAndNewPanelModifierKeepBarSynchronized()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueKey(CtrlO());
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(CtrlO());
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        int read = 0;
        driver.BeforeReadInput = _ =>
        {
            switch (read++)
            {
                case 1:
                    Assert.Equal(FunctionKeyLayer.Control, app.Session.FunctionKeyLayer);
                    break;
                case 2:
                    Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, app.Session.App.WorkspaceMode);
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    break;
                case 3:
                    var release = Assert.IsType<ModifierKeyConsoleInputEvent>(driver.LastDequeuedInput);
                    Assert.Equal(default(ConsoleModifiers), release.Modifiers);
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    break;
                case 4:
                    Assert.Equal(ApplicationWorkspaceMode.Panels, app.Session.App.WorkspaceMode);
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    Assert.Contains("1Help", driver.GetRow(13));
                    break;
                case 5:
                    Assert.Equal(FunctionKeyLayer.Control, app.Session.FunctionKeyLayer);
                    Assert.Contains("SortNm", driver.GetRow(13));
                    break;
                case 6:
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    Assert.Contains("1Help", driver.GetRow(13));
                    break;
            }
        };

        app.Run();

        Assert.Equal(7, read);
        Assert.Equal(string.Empty, GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_RepeatedModifierEventsDoNotCauseRedundantBarWrites()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        int read = 0;
        int writesAfterControl = 0;
        int writesAfterRelease = 0;
        driver.BeforeReadInput = _ =>
        {
            switch (read++)
            {
                case 1:
                    writesAfterControl = driver.WriteAtCallCount;
                    Assert.True(writesAfterControl > 0);
                    break;
                case 2:
                case 3:
                    Assert.Equal(writesAfterControl, driver.WriteAtCallCount);
                    break;
                case 4:
                    writesAfterRelease = driver.WriteAtCallCount;
                    Assert.True(writesAfterRelease > writesAfterControl);
                    break;
                case 5:
                    Assert.Equal(writesAfterRelease, driver.WriteAtCallCount);
                    break;
            }
        };

        app.Run();
        Assert.Equal(6, read);
    }

    [Fact]
    public void Run_CtrlOChordWithoutStandaloneModifierNeverSetsHeldLayer()
    {
        var fs = CreateFileSystem();
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueKey(CtrlO());
        driver.EnqueueKey(CtrlO());
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        int read = 0;
        driver.BeforeReadInput = _ =>
        {
            read++;
            Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
        };

        app.Run();
        Assert.Equal(3, read);
    }

    [Fact]
    public void Run_SearchResultsPanelKeyBarShowsAllowedAndHidesCapabilityBlockedCommands()
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
        Assert.Contains("4Edit", row);
        Assert.Contains("5Copy", row);
        Assert.Contains("8Delete", row);
        Assert.Contains("9ConfMn", row);
        Assert.DoesNotContain("6RenMov", row);
        Assert.DoesNotContain("7MkFold", row);
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
        settings.Application.ConfirmExit = false;
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

    private static ConsoleKeyInfo CtrlO() =>
        new('\u000f', ConsoleKey.O, shift: false, alt: false, control: true);

    private static ConsoleKeyInfo Key(ConsoleKey key, bool control = false, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: control);

    private static MouseConsoleInputEvent LeftMouse(int x, int y, MouseEventKind kind) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
    }

    private static CommandLineState GetCommandLine(Application app)
    {
        return app.Session.CommandLine.State;
    }

    private static void SetFunctionKeyLayer(Application app, FunctionKeyLayer layer)
    {
        app.Session.FunctionKeyLayer = layer;
    }

}
