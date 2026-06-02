using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class NavigateToDirectoryShortcutCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _target;

    public NavigateToDirectoryShortcutCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarShortcutRoot_{Guid.NewGuid():N}");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Execute_EmptySlot_DoesNotNavigate()
    {
        var context = CreateContext(out _, out _);

        new NavigateToDirectoryShortcutCommand()
            .Execute(context, new NavigateToDirectoryShortcutArgs(1));

        Assert.Equal(_root, context.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void Execute_ExistingPath_NavigatesOnlyActivePanelAndKeepsShortcut()
    {
        var context = CreateContext(out _, out _);
        context.Settings.DirectoryShortcuts.Items.Add(Item(1, _target));
        string passiveDirectory = context.PassiveState.CurrentDirectory;

        new NavigateToDirectoryShortcutCommand()
            .Execute(context, new NavigateToDirectoryShortcutArgs(1));

        Assert.Equal(_target, context.ActiveState.CurrentDirectory);
        Assert.Equal(passiveDirectory, context.PassiveState.CurrentDirectory);
        Assert.Single(context.Settings.DirectoryShortcuts.Items);
    }

    [Fact]
    public void Execute_MissingPath_ShowsMessageAndKeepsShortcut()
    {
        var context = CreateContext(out var driver, out _);
        string missingPath = Path.Combine(_root, "missing");
        context.Settings.DirectoryShortcuts.Items.Add(Item(1, missingPath));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        new NavigateToDirectoryShortcutCommand()
            .Execute(context, new NavigateToDirectoryShortcutArgs(1));

        Assert.Equal(_root, context.ActiveState.CurrentDirectory);
        Assert.Single(context.Settings.DirectoryShortcuts.Items);
    }

    private ApplicationCommandContext CreateContext(
        out FakeConsoleDriver driver,
        out AppSettings settings)
    {
        driver = new FakeConsoleDriver();
        settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        fs.AddDirectory(_target);
        var app = new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
        return new ApplicationCommandContext(app);
    }

    private static AppSettings.DirectoryShortcutItem Item(int number, string path) =>
        new()
        {
            Number = number,
            Name = "Target",
            Path = path,
        };

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
