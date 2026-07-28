using CSharpFar.App.Bootstrap;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class DemoModeLocalSourceGuardTests : IDisposable
{
    private readonly string _root;

    public DemoModeLocalSourceGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarDemoGuards_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SearchFilesCommand_CanExecute_FalseWhenLocalSourceIsUnregistered()
    {
        var context = CreateContext();
        context.LeftPanel.CurrentLocation = PanelLocation.Local(_root);

        Assert.False(new SearchFilesCommand().CanExecute(context));
    }

    [Fact]
    public void CompareCommand_CanExecute_FalseWhenPanelsLookLocalButLocalSourceIsUnregistered()
    {
        var context = CreateContext();
        context.LeftPanel.CurrentLocation = PanelLocation.Local(_root);
        context.RightPanel.CurrentLocation = PanelLocation.Local(_root);

        Assert.False(new CompareCommand(CompareCommandKind.Folders).CanExecute(context));
        Assert.False(new CompareCommand(CompareCommandKind.FileSets).CanExecute(context));
    }

    [Fact]
    public void DirectoryHistoryCommand_DoesNotNavigateWhenLocalSourceIsUnregistered()
    {
        var context = CreateContext();
        context.LeftPanel.CurrentLocation = PanelLocation.Local(_root);
        context.History.AddDirectory(new DirectoryHistoryItem { Path = Path.Combine(_root, "missing") });

        new DirectoryHistoryCommand().Execute(context);

        Assert.Equal(PanelLocation.Local(_root), context.LeftPanel.CurrentLocation);
    }

    [Fact]
    public void FileHistoryCommand_DoesNotOpenWhenLocalSourceIsUnregistered()
    {
        var context = CreateContext();
        context.LeftPanel.CurrentLocation = PanelLocation.Local(_root);
        context.History.AddFile(new FileHistoryItem { Path = Path.Combine(_root, "missing.txt") });

        new FileHistoryCommand().Execute(context);

        Assert.Equal(PanelLocation.Local(_root), context.LeftPanel.CurrentLocation);
    }

    [Fact]
    public void NavigateToDirectoryShortcutCommand_CanExecute_FalseWhenLocalSourceIsUnregistered()
    {
        var context = CreateContext();
        context.LeftPanel.CurrentLocation = PanelLocation.Local(_root);
        context.Settings.DirectoryShortcuts.Items.Add(new AppSettings.DirectoryShortcutItem
        {
            Number = 1,
            Name = "Root",
            Path = _root,
        });

        var command = new NavigateToDirectoryShortcutCommand();

        Assert.False(command.CanExecute(context, new NavigateToDirectoryShortcutArgs(1)));
        Assert.False(command.CanExecute(context, new NavigateToCommittedDirectoryShortcutArgs(1, _root, PanelSide.Left)));
    }

    [Fact]
    public void ChangeDirectoryCommandExecutor_StopsBeforePathChecksWhenLocalSourceIsUnregistered()
    {
        var state = new FilePanelState { CurrentLocation = PanelLocation.Local(_root) };
        bool startedWatching = false;
        var executor = new ChangeDirectoryCommandExecutor(
            new PanelController(new FakePanelViewBuilder(new FakeFileSystemService())),
            () => state,
            () => PanelSide.Left,
            () => false,
            () => new AppSettings.PanelOptionsSettings(),
            (_, _) => startedWatching = true);

        bool handled = executor.TryExecute("cd missing");

        Assert.True(handled);
        Assert.False(startedWatching);
        Assert.Equal(PanelLocation.Local(_root), state.CurrentLocation);
    }

    private ApplicationCommandContext CreateContext()
    {
        var settings = new AppSettings();
        string configDirectory = Path.Combine(_root, "config");
        Directory.CreateDirectory(configDirectory);
        string fixture = Path.Combine(_root, "fixture");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "file.txt"), "demo");

        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            new DemoModeServices.DisabledLocalFileSystemService(),
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            new UserMenuStore(configDirectory),
            searchService: new DemoModeServices.DisabledSearchService(),
            sourceRegistry: new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]),
            configDirectory: configDirectory,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixture));
        return services.CommandContext;
    }
}
