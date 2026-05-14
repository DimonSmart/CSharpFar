using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec034PanelQuickSearchTests : IDisposable
{
    private readonly string _root;

    public Spec034PanelQuickSearchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec034_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void HandleKey_AltLetterStartsQuickSearchMovesCursorAndRendersOverlay()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver,
            FileItem("alpha.txt"),
            FileItem("GEMINI.md"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));

        var left = GetLeftPanel(app);
        Assert.Equal("GEMINI.md", left.Items[left.CursorIndex].Name);
        Assert.Equal("g", GetQuickSearchText(app));
        Assert.Equal(string.Empty, GetCommandLine(app).Text);
        Assert.Contains("Search", ComposeRow(driver, y: 8, width: 80), StringComparison.Ordinal);
        Assert.Contains("g", ComposeRow(driver, y: 9, width: 80), StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKey_QuickSearchTypingRefinesWithoutCommandLineText()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver,
            FileItem("ga.txt"),
            FileItem("gemini.md"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        HandleKeyAndRender(app, Key(ConsoleKey.E, keyChar: 'e'));
        HandleKeyAndRender(app, Key(ConsoleKey.M, keyChar: 'm'));

        var left = GetLeftPanel(app);
        Assert.Equal("gemini.md", left.Items[left.CursorIndex].Name);
        Assert.Equal("gem", GetQuickSearchText(app));
        Assert.Equal(string.Empty, GetCommandLine(app).Text);
    }

    [Fact]
    public void HandleKey_QuickSearchNoMatchKeepsCursorOnLastMatch()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver,
            FileItem("gemini.md"),
            FileItem("zeta.txt"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        int matchedCursor = GetLeftPanel(app).CursorIndex;

        HandleKeyAndRender(app, Key(ConsoleKey.X, keyChar: 'x'));

        Assert.Equal(matchedCursor, GetLeftPanel(app).CursorIndex);
        Assert.Equal("gx", GetQuickSearchText(app));
    }

    [Fact]
    public void HandleKey_QuickSearchBackspaceShortensAndMovesToFirstShorterMatch()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver,
            FileItem("ga.txt"),
            FileItem("ge.txt"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        HandleKeyAndRender(app, Key(ConsoleKey.E, keyChar: 'e'));

        Assert.Equal("ge.txt", GetLeftPanel(app).Items[GetLeftPanel(app).CursorIndex].Name);

        HandleKeyAndRender(app, Key(ConsoleKey.Backspace));

        Assert.Equal("g", GetQuickSearchText(app));
        Assert.Equal("ga.txt", GetLeftPanel(app).Items[GetLeftPanel(app).CursorIndex].Name);
    }

    [Fact]
    public void HandleKey_EscapeClosesQuickSearchAndPlainTypingReturnsToCommandLine()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver, FileItem("gemini.md"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        HandleKeyAndRender(app, Key(ConsoleKey.Escape));
        HandleKeyAndRender(app, Key(ConsoleKey.E, keyChar: 'e'));

        Assert.Null(GetQuickSearchState(app));
        Assert.Equal("e", GetCommandLine(app).Text);
    }

    [Fact]
    public void HandleKey_EnterClosesQuickSearchAndContinuesNormalHandling()
    {
        string fullPath = Path.Combine(_root, "gemini.md");
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var launcher = new RecordingFileLauncher();
        var app = CreateApp(driver, [FileItem("gemini.md", fullPath)], launcher);

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        HandleKeyAndRender(app, Key(ConsoleKey.Enter));

        Assert.Null(GetQuickSearchState(app));
        Assert.Equal([fullPath], launcher.OpenedFiles);
    }

    [Fact]
    public void HandleKey_Alt2KeepsReservedViewModeShortcut()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver, FileItem("gemini.md"));

        HandleKeyAndRender(app, Key(ConsoleKey.D2, keyChar: '2', alt: true));

        Assert.Null(GetQuickSearchState(app));
        Assert.Equal(PanelViewMode.BriefTwoColumns, GetLeftViewMode(app));
    }

    [Fact]
    public void HandleKey_TabClosesQuickSearchBeforeNextPlainTyping()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(driver, FileItem("gemini.md"));

        HandleKeyAndRender(app, Key(ConsoleKey.G, alt: true));
        HandleKeyAndRender(app, Key(ConsoleKey.Tab));
        HandleKeyAndRender(app, Key(ConsoleKey.E, keyChar: 'e'));

        Assert.Null(GetQuickSearchState(app));
        Assert.Equal(PanelSide.Right, GetActiveSide(app));
        Assert.Equal("e", GetCommandLine(app).Text);
    }

    private Application CreateApp(FakeConsoleDriver driver, params FilePanelItem[] leftItems) =>
        CreateApp(driver, leftItems, new RecordingFileLauncher());

    private Application CreateApp(
        FakeConsoleDriver driver,
        FilePanelItem[] leftItems,
        IFileLauncher fileLauncher)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, leftItems);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            fileLauncher: fileLauncher);
    }

    private FilePanelItem FileItem(string name, string? fullPath = null) =>
        new()
        {
            Name = name,
            FullPath = fullPath ?? Path.Combine(_root, name),
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static void HandleKeyAndRender(Application app, ConsoleKeyInfo key)
    {
        var method = typeof(Application).GetMethod(
            "HandleKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.HandleKey method not found.");

        bool shouldRender = (bool)method.Invoke(app, [key])!;
        if (shouldRender)
            Render(app);
    }

    private static void Render(Application app)
    {
        var method = typeof(Application).GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.Render method not found.");

        method.Invoke(app, []);
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool alt = false) =>
        new(keyChar, key, shift: false, alt, control: false);

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

    private static FilePanelState GetLeftPanel(Application app)
    {
        var field = typeof(Application).GetField("_left", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._left field not found.");
        return (FilePanelState)field.GetValue(app)!;
    }

    private static PanelSide GetActiveSide(Application app)
    {
        var field = typeof(Application).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._active field not found.");
        return (PanelSide)field.GetValue(app)!;
    }

    private static PanelViewMode GetLeftViewMode(Application app)
    {
        var field = typeof(Application).GetField("_leftViewMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._leftViewMode field not found.");
        return (PanelViewMode)field.GetValue(app)!;
    }

    private static CommandLineState GetCommandLine(Application app)
    {
        var field = typeof(Application).GetField("_cmdLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");
        return (CommandLineState)field.GetValue(app)!;
    }

    private static object? GetQuickSearchState(Application app)
    {
        var field = typeof(Application).GetField("_panelQuickSearch", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._panelQuickSearch field not found.");
        return field.GetValue(app);
    }

    private static string? GetQuickSearchText(Application app)
    {
        object? state = GetQuickSearchState(app);
        if (state is null)
            return null;

        var property = state.GetType().GetProperty("SearchText")
            ?? throw new InvalidOperationException("PanelQuickSearchState.SearchText property not found.");
        return (string)property.GetValue(state)!;
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

    private sealed class RecordingFileLauncher : IFileLauncher
    {
        public List<string> OpenedFiles { get; } = [];

        public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.ShellAssociation;

        public void OpenFile(string fullPath, string workingDirectory) =>
            OpenedFiles.Add(fullPath);
    }
}
