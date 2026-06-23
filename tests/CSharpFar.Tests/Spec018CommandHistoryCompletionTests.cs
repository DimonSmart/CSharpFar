using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec018CommandHistoryCompletionTests : IDisposable
{
    private readonly string _root;

    public Spec018CommandHistoryCompletionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec018_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_HiddenPanels_UpDownBrowseCommandHistory()
    {
        var history = CreateHistory("first", "second");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, control: true, keyChar: '\u000f'));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("second", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_HiddenPanels_UpStopsAtOldestCommand()
    {
        var history = CreateHistory("first", "second");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, control: true, keyChar: '\u000f'));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("first", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_HiddenPanels_DownStopsAtNewestCommand()
    {
        var history = CreateHistory("first", "second");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, control: true, keyChar: '\u000f'));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("second", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_VisiblePanels_EnterOnDefaultNeutralCompletionExecutesTypedCommand()
    {
        var history = CreateHistory("git status", "git commit");
        var shell = new RecordingShellService();
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(KeyChar('i', ConsoleKey.I));
        driver.EnqueueKey(KeyChar('t', ConsoleKey.T));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, shell);
        app.Run();

        Assert.Equal(["git"], shell.ExecutedCommands);
        Assert.Equal(string.Empty, GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_VisiblePanels_EnterAcceptsExplicitlySelectedCompletionWithoutExecuting()
    {
        var history = CreateHistory("git status", "git commit");
        var shell = new RecordingShellService();
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, shell);
        app.Run();

        Assert.Equal("git commit", GetCommandLine(app).Text);
        Assert.Empty(shell.ExecutedCommands);
    }

    [Fact]
    public void ExecuteCommand_AddsCommandHistoryBeforeShellExecution()
    {
        var history = new InMemoryHistoryStore();
        var shell = new RecordingShellService((command, workingDirectory) =>
        {
            var item = Assert.Single(history.GetCommandHistory());
            Assert.Equal("long-running-command", item.Command);
            Assert.Equal(_root, item.WorkingDirectory);
        });
        var driver = new FakeConsoleDriver(width: 100, height: 12);

        var app = CreateApp(driver, history, shell);
        app.ExecuteCommand("long-running-command");

        Assert.Equal(["long-running-command"], shell.ExecutedCommands);
    }

    [Fact]
    public void Run_VisiblePanels_CompletionRendersSingleBorderedList()
    {
        var history = CreateHistory("git status", "git commit", "git branch");
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal('┌', ComposeRow(driver, y: 4, width: 40)[0]);
        Assert.Equal('┐', ComposeRow(driver, y: 4, width: 40)[39]);
        Assert.Equal('│', ComposeRow(driver, y: 5, width: 40)[0]);
        Assert.Equal('│', ComposeRow(driver, y: 5, width: 40)[39]);
        Assert.Equal('└', ComposeRow(driver, y: 9, width: 40)[0]);
        Assert.Equal('┘', ComposeRow(driver, y: 9, width: 40)[39]);
        Assert.DoesNotContain("git", ComposeRow(driver, y: 5, width: 40), StringComparison.Ordinal);
        Assert.Contains("git branch", ComposeRow(driver, y: 6, width: 40), StringComparison.Ordinal);
        Assert.Contains("git commit", ComposeRow(driver, y: 7, width: 40), StringComparison.Ordinal);
        Assert.Contains("git status", ComposeRow(driver, y: 8, width: 40), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_VisiblePanels_MouseClickCompletionAcceptsSuggestionWithoutExecuting()
    {
        var history = CreateHistory("git status", "git commit", "git branch");
        var shell = new RecordingShellService();
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueInput(LeftMouse(2, 6));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, shell);
        app.Run();

        Assert.Equal("git branch", GetCommandLine(app).Text);
        Assert.Empty(shell.ExecutedCommands);
    }

    [Fact]
    public void Run_VisiblePanels_MouseClickNeutralCompletionKeepsTypedCommand()
    {
        var history = CreateHistory("git status", "git commit", "git branch");
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueInput(LeftMouse(2, 5));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_VisiblePanels_EscapeHidesCompletionWithoutClearingCommandLine()
    {
        var history = CreateHistory("git status");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_VisiblePanels_DeleteOnSelectedCompletionRemovesCommandFromHistory()
    {
        var history = CreateHistory("git status", "git commit", "git branch");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
        Assert.DoesNotContain("git branch", app.Session.CommandLine.Completion.Matches);
        Assert.DoesNotContain(history.GetCommandHistory(), item => item.Command == "git branch");
        Assert.Equal(["git status", "git commit"], history.GetCommandHistory().Select(item => item.Command).ToArray());
    }

    [Fact]
    public void Run_VisiblePanels_DeleteOnNeutralCompletionKeepsHistory()
    {
        var history = CreateHistory("git status", "git commit");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
        Assert.Equal(["git status", "git commit"], history.GetCommandHistory().Select(item => item.Command).ToArray());
    }

    [Fact]
    public void Run_VisiblePanels_DeleteBeforeCommandLineEndKeepsHistory()
    {
        var history = CreateHistory("git status", "git commit", "git branch");
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(KeyChar('i', ConsoleKey.I));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.LeftArrow));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
        Assert.Contains(history.GetCommandHistory(), item => item.Command == "git branch");
    }

    [Fact]
    public void Run_ShortConsoleWithMatchingCompletion_DoesNotThrow()
    {
        var history = CreateHistory("git status");
        var driver = new FakeConsoleDriver(width: 40, height: 2);
        driver.EnqueueKey(KeyChar('g', ConsoleKey.G));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(driver, history, new RecordingShellService());
        app.Run();

        Assert.Equal("g", GetCommandLine(app).Text);
    }

    private Application CreateApp(
        FakeConsoleDriver driver,
        InMemoryHistoryStore history,
        RecordingShellService shell)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            shell,
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
        return app.Session.CommandLine.State;
    }

    private static string ComposeRow(FakeConsoleDriver driver, int y, int width)
    {
        var row = Enumerable.Repeat(' ', width).ToArray();
        foreach (var record in driver.WriteRecords.Where(record => record.Y == y))
        {
            for (int i = 0; i < record.Text.Length && record.X + i < width; i++)
                row[record.X + i] = record.Text[i];
        }

        return new string(row);
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        bool control = false,
        char keyChar = '\0') =>
        new(keyChar, key, shift: false, alt: false, control: control);

    private static ConsoleKeyInfo KeyChar(char ch, ConsoleKey key) =>
        new(ch, key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private sealed class RecordingShellService : IShellService
    {
        private readonly List<string> _executedCommands = [];
        private readonly Action<string, string>? _onExecute;

        public RecordingShellService(Action<string, string>? onExecute = null)
        {
            _onExecute = onExecute;
        }

        public IReadOnlyList<string> ExecutedCommands => _executedCommands;

        public void Execute(string command, string workingDirectory)
        {
            _onExecute?.Invoke(command, workingDirectory);
            _executedCommands.Add(command);
        }
    }

}
