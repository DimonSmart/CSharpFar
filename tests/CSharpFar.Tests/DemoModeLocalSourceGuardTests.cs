using CSharpFar.App.Bootstrap;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.DirectoryShortcuts;
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
        var services = CreateServices();
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Local(_root);

        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.Search));
    }

    [Fact]
    public void CompareCommand_CanExecute_FalseWhenPanelsLookLocalButLocalSourceIsUnregistered()
    {
        var services = CreateServices();
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Local(_root);
        services.Session.Panels.Right.CurrentLocation = PanelLocation.Local(_root);

        Assert.False(services.CommandRegistry.CanExecute(ApplicationCommandIds.CompareFolders));
        Assert.False(services.CommandRegistry.CanExecute(ApplicationCommandIds.CompareFileSets));
    }

    [Fact]
    public void DirectoryHistoryCommand_DoesNotNavigateWhenLocalSourceIsUnregistered()
    {
        var services = CreateServices();
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Local(_root);
        services.History.AddDirectory(new DirectoryHistoryItem { Path = Path.Combine(_root, "missing") });

        services.CommandRegistry.Execute(FunctionKeyCommandIds.DirectoryHistory);

        Assert.Equal(PanelLocation.Local(_root), services.Session.Panels.Left.CurrentLocation);
    }

    [Fact]
    public void FileHistoryCommand_DoesNotOpenWhenLocalSourceIsUnregistered()
    {
        var services = CreateServices();
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Local(_root);
        services.History.AddFile(new FileHistoryItem { Path = Path.Combine(_root, "missing.txt") });

        services.CommandRegistry.Execute(FunctionKeyCommandIds.FileHistory);

        Assert.Equal(PanelLocation.Local(_root), services.Session.Panels.Left.CurrentLocation);
    }

    [Fact]
    public void NavigateToDirectoryShortcutCommand_CanExecute_FalseWhenLocalSourceIsUnregistered()
    {
        var services = CreateServices();
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Local(_root);
        services.Settings.DirectoryShortcuts.Items.Add(new AppSettings.DirectoryShortcutItem
        {
            Number = 1,
            Name = "Root",
            Path = _root,
        });

        Assert.True(services.CommandRegistry.TryGetCommand(DirectoryShortcutCommandIds.Navigate, out var command));

        Assert.False(command.CanExecute(new NavigateToDirectoryShortcutArgs(1)));
        Assert.False(command.CanExecute(new NavigateToCommittedDirectoryShortcutArgs(1, _root, PanelSide.Left)));
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

    private ApplicationServices CreateServices()
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
        return services;
    }
}
