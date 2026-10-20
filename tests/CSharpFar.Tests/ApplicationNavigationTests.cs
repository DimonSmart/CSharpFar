using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed partial class ApplicationNavigationTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationNavigationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarNavigationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Run_EnterOnParentDirectory_PositionsCursorOnChildDirectory()
    {
        string childName = "Sub25";
        string childPath = Path.Combine(_tempDir, childName);
        Directory.CreateDirectory(childPath);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(childPath, new FilePanelItem
        {
            Name = "..",
            FullPath = _tempDir,
            IsDirectory = true,
            IsParentDirectory = true,
        });

        var parentItems = Enumerable.Range(0, 25)
            .Select(i => new FilePanelItem
            {
                Name = $"Dir{i:D2}",
                FullPath = Path.Combine(_tempDir, $"Dir{i:D2}"),
                IsDirectory = true,
            })
            .Append(new FilePanelItem
            {
                Name = childName,
                FullPath = childPath,
                IsDirectory = true,
            })
            .ToArray();
        fs.AddDirectory(_tempDir, parentItems);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, childPath);
        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(_tempDir, left.CurrentDirectory);
        Assert.Equal(childName, left.Items[left.CursorIndex].Name);
        Assert.True(left.ScrollOffset <= left.CursorIndex);
        Assert.True(left.CursorIndex < left.ScrollOffset + 6);
    }

    [Fact]
    public void Run_LeftRightInHiddenPanelMode_EditCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 160, height: 10);
        driver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('b', ConsoleKey.B, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();

        var commandLine = GetCommandLine(app);
        Assert.Equal("abXc", commandLine.Text);
        Assert.Equal(3, commandLine.CursorPosition);
        Assert.Equal(8, driver.CursorY);
    }

    [Fact]
    public void HandleKey_CtrlAWithCommandTextSelectsCommandLineInsteadOfPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "item.txt", FullPath = Path.Combine(_tempDir, "item.txt"), IsDirectory = false });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.A, keyChar: 'a'));
        HandleKeyAndRender(app, Key(ConsoleKey.B, keyChar: 'b'));
        HandleKeyAndRender(app, Key(ConsoleKey.A, keyChar: '\u0001', control: true));

        var commandLine = GetCommandLine(app);
        Assert.True(commandLine.HasSelection);
        Assert.Equal(0, commandLine.SelectionStart);
        Assert.Equal(2, commandLine.SelectionLength);
        Assert.Empty(GetLeftPanel(app).SelectedPaths);

        HandleKeyAndRender(app, Key(ConsoleKey.X, keyChar: 'x'));

        Assert.Equal("x", commandLine.Text);
        Assert.False(commandLine.HasSelection);
    }

    [Fact]
    public void Run_CtrlAInHiddenPanelModeReplacesCommandLineText()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.A, keyChar: 'a'));
        driver.EnqueueKey(Key(ConsoleKey.B, keyChar: 'b'));
        driver.EnqueueKey(Key(ConsoleKey.C, keyChar: 'c'));
        driver.EnqueueKey(Key(ConsoleKey.A, keyChar: '\u0001', control: true));
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: 'x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();

        Assert.Equal("x", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlOAfterViewportOriginChange_DoesNotRestoreStaleUnderlay()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.WriteAt(0, 0, "OLD-SHELL".AsSpan());
        driver.BeforeReadInput = d =>
        {
            d.SetViewportOrigin(0, 1);
            d.BeforeReadInput = afterCtrlO =>
            {
                Assert.DoesNotContain("OLD-SHELL", afterCtrlO.GetRow(0));
            };
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        FakeConsoleDriver driver,
        string startDirectory,
        IShellService? shell = null,
        ITerminalScreenMode? terminalScreenMode = null)
    {
        var settings = new AppSettings();
        settings.Application.ConfirmExit = false;
        settings.Panels.LeftStartDirectory = startDirectory;
        settings.Panels.RightStartDirectory = startDirectory;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            shell ?? new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            terminalScreenMode: terminalScreenMode);
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey consoleKey,
        char keyChar = '\0',
        bool control = false,
        bool shift = false) =>
        new(keyChar, consoleKey, shift, alt: false, control);

    private static ConsoleKeyInfo WorkspaceModeKey(string key) => key switch
    {
        "CtrlF1" => Key(ConsoleKey.F1, control: true),
        "CtrlF2" => Key(ConsoleKey.F2, control: true),
        "CtrlO" => Key(ConsoleKey.O, keyChar: '\u000f', control: true),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown workspace mode key."),
    };

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
    }

    private static FilePanelState GetRightPanel(Application app)
    {
        return app.Session.Panels.Right;
    }

    private static void HandleKeyAndRender(Application app, ConsoleKeyInfo key)
    {
        var composition = (UiCompositionHost)(typeof(Application).GetField(
            "_composition",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app)
            ?? throw new InvalidOperationException("Application composition host not found."));
        var dispatcher = (ApplicationInputDispatcher)(typeof(Application).GetField(
            "_applicationInputDispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app)
            ?? throw new InvalidOperationException("Application input dispatcher not found."));
        var surface = GetApplicationSurface(app);

        if (!surface.HasCommittedFrame)
            Render(app);

        UiInputResult routed = composition.DispatchInput(new KeyConsoleInputEvent(key));
        bool shouldRender = routed.Invalidate;
        if (surface.TryTakeInput(out var packet))
            shouldRender |= dispatcher.Handle(packet).ShouldRender;
        if (shouldRender)
            Render(app);
    }

    private static bool HandleMouse(Application app, MouseConsoleInputEvent mouse)
    {
        var dispatcher = (ApplicationInputDispatcher)(typeof(Application).GetField(
            "_applicationInputDispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app)
            ?? throw new InvalidOperationException("Application input dispatcher not found."));

        var runtime = typeof(Application).GetField(
            "_runtime",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app)
            ?? throw new InvalidOperationException("Application runtime not found.");
        var surface = (ApplicationUiSurface)(runtime.GetType().GetField(
            "_applicationSurface",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(runtime)
            ?? throw new InvalidOperationException("Application surface not found."));

        UiTargetId? target = surface.CommittedInteractionFrame.TryHitTest(mouse.X, mouse.Y, out var region)
            ? region.Target
            : null;
        var routeKind = target is null ? UiInputRouteKind.Layer : UiInputRouteKind.HitTarget;

        return dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            mouse,
            surface.CommittedFrame,
            target,
            routeKind)).ShouldRender;
    }

    private static ApplicationUiSurface GetApplicationSurface(Application app)
    {
        var runtime = typeof(Application).GetField(
            "_runtime",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app)
            ?? throw new InvalidOperationException("Application runtime not found.");
        return (ApplicationUiSurface)(runtime.GetType().GetField(
            "_applicationSurface",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(runtime)
            ?? throw new InvalidOperationException("Application surface not found."));
    }

    private static void Render(Application app)
    {
        var method = typeof(Application).GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.Render method not found.");

        method.Invoke(app, []);
    }

    private static void RenderCommandLineOnly(Application app)
    {
        var method = typeof(Application).GetMethod(
            "RenderCommandLineOnly",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.RenderCommandLineOnly method not found.");

        method.Invoke(app, []);
    }

    private static void AssertOperationOrder(FakeConsoleDriver driver, params string[] expected)
    {
        int previous = -1;
        foreach (string operation in expected)
        {
            int current = -1;
            for (int i = previous + 1; i < driver.OperationLog.Count; i++)
            {
                if (driver.OperationLog[i] != operation)
                    continue;

                current = i;
                break;
            }

            Assert.True(
                current > previous,
                $"Expected operation '{operation}' after index {previous}. Log: {string.Join(", ", driver.OperationLog)}");
            previous = current;
        }
    }

    private static IReadOnlyList<int> RowsContainingCommandPrompt(FakeConsoleDriver driver)
    {
        var rows = new List<int>();
        for (int row = 0; row < driver.GetSize().Height; row++)
        {
            if (driver.GetRow(row).Contains('>'))
                rows.Add(row);
        }

        return rows;
    }

    // FakeConsoleDriver consumes read hooks once, so explicitly rearm after each read.
    private static void OnEveryRead(FakeConsoleDriver driver, Action<FakeConsoleDriver> callback)
    {
        driver.BeforeReadInput = Check;

        void Check(FakeConsoleDriver current)
        {
            callback(current);
            current.BeforeReadInput = Check;
        }
    }

    private static void BeforeEachRead(FakeConsoleDriver driver, params Action<FakeConsoleDriver>[] actions)
    {
        int index = 0;
        driver.BeforeReadInput = RunNext;

        void RunNext(FakeConsoleDriver current)
        {
            actions[index++](current);
            if (index < actions.Length)
                current.BeforeReadInput = RunNext;
        }
    }

    private static PanelSide GetActiveSide(Application app)
    {
        return app.Session.Panels.ActiveSide;
    }

    private static void AssertLeftPanelOnly(FakeConsoleDriver driver)
    {
        Assert.Equal('╔', driver.GetCell(0, 0).Character);
        Assert.Equal('╗', driver.GetCell(39, 0).Character);
        Assert.Equal('╢', driver.GetCell(39, 6).Character);
        Assert.Equal(' ', driver.GetCell(40, 0).Character);
    }

    private static void AssertRightPanelOnly(FakeConsoleDriver driver)
    {
        Assert.Equal(' ', driver.GetCell(0, 0).Character);
        Assert.Equal(' ', driver.GetCell(39, 0).Character);
        Assert.Equal('╔', driver.GetCell(40, 0).Character);
        Assert.Equal('╟', driver.GetCell(40, 6).Character);
    }

    private static void AssertNoPanels(FakeConsoleDriver driver)
    {
        Assert.Equal(' ', driver.GetCell(0, 0).Character);
        Assert.Equal(' ', driver.GetCell(39, 0).Character);
        Assert.Equal(' ', driver.GetCell(40, 0).Character);
    }

    private static void AssertBothPanels(FakeConsoleDriver driver)
    {
        Assert.Equal('╔', driver.GetCell(0, 0).Character);
        Assert.Equal('╗', driver.GetCell(39, 0).Character);
        Assert.Equal('╔', driver.GetCell(40, 0).Character);
    }

    private static void AssertVisiblePanels(FakeConsoleDriver driver, string expectedPanels)
    {
        switch (expectedPanels)
        {
            case "both":
                AssertBothPanels(driver);
                break;
            case "left":
                AssertLeftPanelOnly(driver);
                break;
            case "right":
                AssertRightPanelOnly(driver);
                break;
            case "none":
                AssertNoPanels(driver);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(expectedPanels),
                    expectedPanels,
                    "Unknown visible panel expectation.");
        }
    }

    private static MouseConsoleInputEvent LeftMouse(int x, int y, MouseEventKind kind) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static CommandLineState GetCommandLine(Application app)
    {
        return app.Session.CommandLine.State;
    }

    private sealed class RecordingShellService : IShellService
    {
        private readonly Action<string, string>? _onExecute;

        public RecordingShellService(Action<string, string>? onExecute = null)
        {
            _onExecute = onExecute;
        }

        public List<(string Command, string WorkingDirectory)> Commands { get; } = [];

        public void Execute(string command, string workingDirectory)
        {
            Commands.Add((command, workingDirectory));
            _onExecute?.Invoke(command, workingDirectory);
        }
    }

}
