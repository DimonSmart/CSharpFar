using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec027FarCommandLineShortcutTests : IDisposable
{
    private readonly string _root;
    private readonly string _leftRoot;
    private readonly string _rightRoot;

    public Spec027FarCommandLineShortcutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec027_{Guid.NewGuid():N}");
        _leftRoot = Path.Combine(_root, "left");
        _rightRoot = Path.Combine(_root, "right panel");
        Directory.CreateDirectory(_leftRoot);
        Directory.CreateDirectory(_rightRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_CtrlEnterInsertsQuotedCurrentItemName()
    {
        var fs = CreateFileSystem(
            leftItems:
            [
                new FilePanelItem
                {
                    Name = "two words.txt",
                    FullPath = Path.Combine(_leftRoot, "two words.txt"),
                    IsDirectory = false,
                },
            ]);
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal("\"two words.txt\"", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlFInsertsActivePanelItemFullPath()
    {
        string activeItemPath = Path.Combine(_rightRoot, "active item.txt");
        var fs = CreateFileSystem(
            rightItems:
            [
                new FilePanelItem
                {
                    Name = "active item.txt",
                    FullPath = activeItemPath,
                    IsDirectory = false,
                },
            ]);
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F, keyChar: '\u0006', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal($"\"{activeItemPath}\"", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlBracketInsertsRightAndLeftPanelPaths()
    {
        var fs = CreateFileSystem();
        var rightDriver = new FakeConsoleDriver(width: 100, height: 12);
        rightDriver.EnqueueKey(Key(ConsoleKey.Oem4, keyChar: '\u001b', control: true));
        rightDriver.EnqueueKey(Key(ConsoleKey.F10));

        var rightApp = CreateApp(fs, rightDriver, new InMemoryHistoryStore());
        rightApp.Run();

        Assert.Equal(_leftRoot + Path.DirectorySeparatorChar, GetCommandLine(rightApp).Text);

        var leftDriver = new FakeConsoleDriver(width: 100, height: 12);
        leftDriver.EnqueueKey(Key(ConsoleKey.Oem6, keyChar: '\u001d', control: true));
        leftDriver.EnqueueKey(Key(ConsoleKey.F10));

        var leftApp = CreateApp(fs, leftDriver, new InMemoryHistoryStore());
        leftApp.Run();

        Assert.Equal($"\"{_rightRoot + Path.DirectorySeparatorChar}\"", GetCommandLine(leftApp).Text);
    }

    [Fact]
    public void Run_CtrlEReplacesTypedTextAndMovesToOlderHistory()
    {
        var history = CreateHistory("first", "second");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.T, keyChar: 't'));
        driver.EnqueueKey(Key(ConsoleKey.E, keyChar: '\u0005', control: true));
        driver.EnqueueKey(Key(ConsoleKey.E, keyChar: '\u0005', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, history);
        app.Run();

        Assert.Equal("first", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlXFirstPressUsesNewestAndThenMovesNewer()
    {
        var history = CreateHistory("first", "second", "third");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.T, keyChar: 't'));
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: '\u0018', control: true));
        driver.EnqueueKey(Key(ConsoleKey.E, keyChar: '\u0005', control: true));
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: '\u0018', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, history);
        app.Run();

        Assert.Equal("third", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CommandLineCtrlArrowMovesByWord()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        EnqueueText(driver, "alpha beta");
        driver.EnqueueKey(Key(ConsoleKey.LeftArrow, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal(6, GetCommandLine(app).CursorPosition);
    }

    [Fact]
    public void Run_CommandLineCtrlASelectsAllWhenTextExists()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        EnqueueText(driver, "alpha beta");
        driver.EnqueueKey(Key(ConsoleKey.A, keyChar: '\u0001', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal("alpha beta", GetCommandLine(app).SelectedText);
    }

    [Fact]
    public void Run_CommandLineShiftArrowSelectsText()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        EnqueueText(driver, "abc");
        driver.EnqueueKey(Key(ConsoleKey.LeftArrow, shift: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal("c", GetCommandLine(app).SelectedText);
    }

    [Fact]
    public void Run_CommandLineMouseDragSelectsText()
    {
        var driver = new FakeConsoleDriver(width: 120, height: 12);
        EnqueueText(driver, "abcdef");

        int promptLength = _leftRoot.Length + 1;
        int commandLineRow = 10;
        driver.EnqueueInput(new MouseConsoleInputEvent(
            promptLength + 1,
            commandLineRow,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            promptLength + 4,
            commandLineRow,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, new InMemoryHistoryStore());
        app.Run();

        Assert.Equal("bcd", GetCommandLine(app).SelectedText);
    }

    private FakeFileSystemService CreateFileSystem(
        FilePanelItem[]? leftItems = null,
        FilePanelItem[]? rightItems = null)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_leftRoot, leftItems ?? []);
        fs.AddDirectory(_rightRoot, rightItems ?? []);
        return fs;
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        FakeConsoleDriver driver,
        InMemoryHistoryStore history)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _leftRoot;
        settings.Panels.RightStartDirectory = _rightRoot;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            history,
            settings);
    }

    private static InMemoryHistoryStore CreateHistory(params string[] commands)
    {
        var history = new InMemoryHistoryStore();
        foreach (string command in commands)
        {
            history.AddCommand(new CommandHistoryItem
            {
                Command = command,
                WorkingDirectory = @"C:\",
            });
        }

        return history;
    }

    private static CommandLineState GetCommandLine(Application app)
    {
        var field = typeof(Application).GetField(
            "_cmdLine",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");

        return (CommandLineState)field.GetValue(app)!;
    }

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
        {
            var key = ch == ' '
                ? ConsoleKey.Spacebar
                : (ConsoleKey)char.ToUpperInvariant(ch);
            driver.EnqueueKey(Key(key, keyChar: ch));
        }
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool control = false,
        bool shift = false) =>
        new(keyChar, key, shift, alt: false, control);

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
