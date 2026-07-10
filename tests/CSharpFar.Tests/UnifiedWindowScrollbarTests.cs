using CSharpFar.App;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Menu;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class UnifiedWindowScrollbarTests
{
    [Fact]
    public void ScrollableListMouse_EmptyList_KeepsNoSelectionAndZeroScrollTop()
    {
        var bounds = new Rect(0, 0, 1, 5);
        int selectedIndex = 0;
        int firstVisibleIndex = 3;
        ScrollBarDragState? dragState = new(bounds, 0, 5, 0);

        bool handled = ScrollableListMouseHandler.TryHandleScrollbarMouse(
            new MouseConsoleInputEvent(0, 2, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            bounds,
            totalItems: 0,
            viewportItems: 5,
            ref selectedIndex,
            ref firstVisibleIndex,
            ref dragState);

        Assert.True(handled);
        Assert.Equal(-1, selectedIndex);
        Assert.Equal(0, firstVisibleIndex);
        Assert.Null(dragState);
    }

    [Fact]
    public void ScrollbarMouse_ClickTopArrow_DecreasesFirstVisibleIndex()
    {
        int firstVisibleIndex = 5;
        ScrollBarDragState? dragState = null;

        bool handled = ScrollBarMouseHandler.TryHandleMouse(
            new MouseConsoleInputEvent(0, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            new Rect(0, 0, 1, 10),
            totalItems: 30,
            viewportItems: 5,
            ref firstVisibleIndex,
            ref dragState);

        Assert.True(handled);
        Assert.Equal(4, firstVisibleIndex);
        Assert.Null(dragState);
    }

    [Fact]
    public void ScrollbarMouse_ClickTrackBelowThumb_MovesByPage()
    {
        int firstVisibleIndex = 0;
        ScrollBarDragState? dragState = null;

        bool handled = ScrollBarMouseHandler.TryHandleMouse(
            new MouseConsoleInputEvent(0, 6, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            new Rect(0, 0, 1, 10),
            totalItems: 30,
            viewportItems: 5,
            ref firstVisibleIndex,
            ref dragState);

        Assert.True(handled);
        Assert.Equal(5, firstVisibleIndex);
        Assert.Null(dragState);
    }

    [Fact]
    public void ScrollbarMouse_DragThumb_MapsPointerToFirstVisibleIndex()
    {
        int firstVisibleIndex = 0;
        ScrollBarDragState? dragState = null;
        var bounds = new Rect(0, 0, 1, 12);

        Assert.True(ScrollBarMouseHandler.TryHandleMouse(
            new MouseConsoleInputEvent(0, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            bounds,
            totalItems: 100,
            viewportItems: 10,
            ref firstVisibleIndex,
            ref dragState));
        Assert.NotNull(dragState);

        Assert.True(ScrollBarMouseHandler.TryHandleMouse(
            new MouseConsoleInputEvent(0, 9, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None),
            bounds,
            totalItems: 100,
            viewportItems: 10,
            ref firstVisibleIndex,
            ref dragState));

        Assert.True(firstVisibleIndex > 50);
    }

    [Fact]
    public void PopupRenderer_VerticalScrollState_DrawsScrollbarOnSingleBorder()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.Blue);

        new PopupRenderer().RenderPopup(
            screen,
            new Rect(2, 1, 8, 5),
            new PopupRenderOptions
            {
                BorderStyle = style,
                BackgroundStyle = style,
                ShadowStyle = style,
                DrawShadow = false,
                VerticalScrollState = new ScrollState
                {
                    TotalItems = 10,
                    ViewportItems = 3,
                    FirstVisibleIndex = 0,
                },
            },
            (_, _) => { });

        Assert.Equal('▲', driver.GetCell(9, 2).Character);
        Assert.Equal('▼', driver.GetCell(9, 4).Character);
    }

    [Fact]
    public void DialogFrameRenderer_VerticalScrollState_DrawsScrollbarOnDoubleBorder()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.Blue);
        var options = new PopupRenderOptions
        {
            BorderStyle = style,
            BackgroundStyle = style,
            ShadowStyle = style,
            DrawShadow = false,
        };

        new DialogFrameRenderer().RenderFrame(
            screen,
            new Rect(2, 1, 8, 5),
            "Test",
            doubleBorder: true,
            options,
            new ScrollState
            {
                TotalItems = 10,
                ViewportItems = 3,
                FirstVisibleIndex = 0,
            },
            (_, _) => { });

        Assert.Equal('▲', driver.GetCell(9, 2).Character);
        Assert.Equal('▼', driver.GetCell(9, 4).Character);
    }

    [Fact]
    public void CommandCompletion_Overflow_DrawsScrollbarOnPopupBorder()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        var screen = new ScreenRenderer(driver);
        var commands = Enumerable.Range(0, 10).Select(i => $"git command {i}").ToArray();

        new CommandHistoryCompletionRenderer(screen).Render(
            commandLineRow: 10,
            totalWidth: 40,
            commands,
            selectedIndex: 0);

        Assert.Equal('▲', driver.GetCell(39, 1).Character);
        Assert.Equal('▼', driver.GetCell(39, 8).Character);
    }

    [Fact]
    public void SearchProgress_OverflowResults_DrawsScrollbar()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueKeysWhenWriteContains(
            driver,
            "found09.txt",
            KeyChar('S', ConsoleKey.S),
            Key(ConsoleKey.Enter));

        _ = new SearchProgressDialog(screen, new ManyResultsSearchService())
            .Show(new SearchRequest
            {
                RootPath = @"C:\root",
                FileMaskExpression = "*.txt",
                Scope = SearchScope.CurrentDirectoryRecursive,
                MaxDegreeOfParallelism = 1,
            });

        AssertScrollbarWasWritten(driver);
    }

    [Fact]
    public void Application_PanelScrollbarBottomArrow_ClickScrollsPanel()
    {
        string root = Directory.GetCurrentDirectory();
        var items = Enumerable.Range(0, 40)
            .Select(i => new FilePanelItem
            {
                Name = $"file{i:D2}.txt",
                FullPath = Path.Combine(root, $"file{i:D2}.txt"),
                IsDirectory = false,
            })
            .ToArray();
        var fs = new FakeFileSystemService();
        fs.AddDirectory(root, items);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        int panelHeight = 23;
        int leftWidth = 40;
        int visibleRows = PanelRenderer.VisibleRows(
            new Rect(0, 0, leftWidth, panelHeight),
            settings.Panels.Options);
        driver.EnqueueInput(new MouseConsoleInputEvent(
            leftWidth - 1,
            visibleRows,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            settings: settings);
        app.Run();

        Assert.True(GetLeftPanel(app).ScrollOffset > 0);
    }

    [Fact]
    public void HistoryDialogs_Overflow_DrawScrollbar()
    {
        var historyDriver = new FakeConsoleDriver(width: 80, height: 25);
        historyDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = new HistoryDialog(ModalTestHost.Create(historyDriver)).Show(
            Enumerable.Range(0, 20)
                .Select(i => new CommandHistoryItem { Command = $"cmd-{i}", WorkingDirectory = @"C:\" })
                .ToArray());

        var directoryDriver = new FakeConsoleDriver(width: 80, height: 25);
        directoryDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = new DirectoryHistoryDialog(ModalTestHost.Create(directoryDriver)).Show(
            Enumerable.Range(0, 20)
                .Select(i => new DirectoryHistoryItem { Path = $@"C:\dir-{i}" })
                .ToArray());

        var fileDriver = new FakeConsoleDriver(width: 80, height: 25);
        fileDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = new FileHistoryDialog(ModalTestHost.Create(fileDriver)).Show(
            Enumerable.Range(0, 20)
                .Select(i => new FileHistoryItem { Path = $@"C:\file-{i}.txt" })
                .ToArray());

        AssertScrollbarWasWritten(historyDriver);
        AssertScrollbarWasWritten(directoryDriver);
        AssertScrollbarWasWritten(fileDriver);
    }

    [Fact]
    public void DriveAndUserMenuDialogs_Overflow_DrawScrollbar()
    {
        var driveDriver = new FakeConsoleDriver(width: 80, height: 10);
        driveDriver.EnqueueKey(Key(ConsoleKey.Escape));
        var driveScreen = new ScreenRenderer(driveDriver);
        _ = new DriveDialog(driveScreen, ModalTestHost.Create(driveScreen)).Show(
            Enumerable.Range(0, 20)
                .Select(i => new VolumeSelectionItem
                {
                    Label = $"Drive {i}",
                    Shortcut = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Action = VolumeSelectionAction.OpenVolume,
                })
                .ToArray());

        var userMenuDriver = new FakeConsoleDriver(width: 80, height: 25);
        userMenuDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = new UserMenuDialog(ModalTestHost.Create(userMenuDriver)).Show(
            Enumerable.Range(0, 20)
                .Select(i => new UserMenuItem { Title = $"Item {i}", Command = $"command-{i}" })
                .ToArray());

        AssertScrollbarWasWritten(driveDriver);
        AssertScrollbarWasWritten(userMenuDriver);
    }

    [Fact]
    public void DropdownMenu_Overflow_DrawsScrollbarAndKeepsActiveItemVisible()
    {
        var definition = LargeMenuDefinition();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
            ActiveDropdownItemIndex = 6,
        };
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 40, 6), definition, state);
        var driver = new FakeConsoleDriver(width: 40, height: 6);
        var screen = new ScreenRenderer(driver);

        new DropdownMenuRenderer().Render(screen, definition, state, layout, MenuOptions());

        Assert.Equal('▲', driver.GetCell(layout.DropdownBounds!.Value.Right - 1, layout.DropdownBounds.Value.Y + 1).Character);
        Assert.Contains("Item 6", driver.GetRegionText(layout.DropdownBounds.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void ShortFormDialogs_DrawScrollbarAndReturn()
    {
        var searchDriver = new FakeConsoleDriver(width: 80, height: 11);
        searchDriver.EnqueueKey(Key(ConsoleKey.F10));
        var searchResult = new SearchDialog(new ScreenRenderer(searchDriver)).Show(@"C:\root");

        var operationDriver = new FakeConsoleDriver(width: 80, height: 11);
        operationDriver.EnqueueKey(Key(ConsoleKey.F10));
        var operationResult = new FileOperationDialog(new ScreenRenderer(operationDriver)).ShowCopy(
            [@"C:\root\file.txt"],
            @"C:\target",
            new FileOperationOptions());

        var settingsDriver = new FakeConsoleDriver(width: 80, height: 8);
        settingsDriver.EnqueueKey(Key(ConsoleKey.F10));
        var settingsResult = new SettingsDialog(new ScreenRenderer(settingsDriver)).Show(
            PanelViewMode.Full,
            PanelViewMode.Full,
            "Default",
            fileHighlightingEnabled: true,
            editorSyntaxHighlightingEnabled: true);

        Assert.NotNull(searchResult);
        Assert.NotNull(operationResult);
        Assert.NotNull(settingsResult);
        AssertScrollbarWasWritten(searchDriver);
        AssertScrollbarWasWritten(operationDriver);
        AssertScrollbarWasWritten(settingsDriver);
    }

    private static MenuBarDefinition LargeMenuDefinition() =>
        new()
        {
            Items =
            [
                new TopMenuItemDefinition
                {
                    Id = "Large",
                    Text = "Large",
                    HotChar = 'L',
                    Children = Enumerable.Range(0, 10)
                        .Select(i => new MenuItemDefinition
                        {
                            Id = $"item-{i}",
                            Text = $"Item {i}",
                            HotChar = i.ToString(System.Globalization.CultureInfo.InvariantCulture)[0],
                            CommandId = $"item-{i}",
                        })
                        .ToArray(),
                },
            ],
        };

    private static MenuRenderOptions MenuOptions() =>
        new()
        {
            MenuBarNormalStyle = new CellStyle(ConsoleColor.Black, ConsoleColor.DarkCyan),
            MenuBarActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            NormalStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkCyan),
            ActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            HighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkCyan),
            ActiveHighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black),
            DisabledStyle = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.DarkCyan),
            BorderStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkCyan),
            ShadowStyle = new CellStyle(ConsoleColor.Red, ConsoleColor.Yellow),
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo KeyChar(char keyChar, ConsoleKey key) =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private static void AssertScrollbarWasWritten(FakeConsoleDriver driver)
    {
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("▲", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("▼", StringComparison.Ordinal));
    }

    private static void EnqueueKeysWhenWriteContains(
        FakeConsoleDriver driver,
        string text,
        params ConsoleKeyInfo[] keys)
    {
        bool enqueued = false;
        driver.Wrote += OnWrote;

        void OnWrote(FakeConsoleDriver.WriteRecord record)
        {
            if (enqueued || !record.Text.Contains(text, StringComparison.Ordinal))
                return;

            enqueued = true;
            driver.Wrote -= OnWrote;
            foreach (var key in keys)
                driver.EnqueueKey(key);
        }
    }

    private sealed class ManyResultsSearchService : ISearchService
    {
        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < 20; i++)
            {
                progress?.Report(new SearchProgress
                {
                    CurrentPath = request.RootPath,
                    ScannedFiles = i + 1,
                    MatchedItems = i + 1,
                });

                yield return new SearchResultItem
                {
                    FullPath = $@"C:\root\found{i:D2}.txt",
                    Name = $"found{i:D2}.txt",
                    Kind = SearchResultItemKind.File,
                    Size = 1,
                    LastWriteTime = new DateTime(2026, 1, 1),
                    Attributes = FileAttributes.Archive,
                };
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
    }
}
