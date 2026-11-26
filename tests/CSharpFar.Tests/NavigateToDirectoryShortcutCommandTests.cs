using CSharpFar.App.Bootstrap;
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
    private readonly string _otherTarget;

    public NavigateToDirectoryShortcutCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarShortcutRoot_{Guid.NewGuid():N}");
        _target = Path.Combine(_root, "target");
        _otherTarget = Path.Combine(_root, "other");
        Directory.CreateDirectory(_target);
        Directory.CreateDirectory(_otherTarget);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Execute_EmptySlot_DoesNotNavigate()
    {
        var services = CreateServices(out _, out _);

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToDirectoryShortcutArgs(1));
        services.WaitForPanelLoads();

        Assert.Equal(_root, services.PanelWorkspace.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void Execute_ExistingPath_NavigatesOnlyActivePanelAndKeepsShortcut()
    {
        var services = CreateServices(out _, out _);
        services.Settings.DirectoryShortcuts.Items.Add(Item(1, _target));
        string passiveDirectory = services.PanelWorkspace.PassiveState.CurrentDirectory;

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToDirectoryShortcutArgs(1));
        services.WaitForPanelLoads();

        Assert.Equal(_target, services.PanelWorkspace.ActiveState.CurrentDirectory);
        Assert.Equal(passiveDirectory, services.PanelWorkspace.PassiveState.CurrentDirectory);
        Assert.Single(services.Settings.DirectoryShortcuts.Items);
    }

    [Fact]
    public void Execute_MissingPath_ShowsMessageAndKeepsShortcut()
    {
        var services = CreateServices(out var driver, out _);
        string missingPath = Path.Combine(_root, "missing");
        services.Settings.DirectoryShortcuts.Items.Add(Item(1, missingPath));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToDirectoryShortcutArgs(1));
        services.WaitForPanelLoads();

        Assert.Equal(_root, services.PanelWorkspace.ActiveState.CurrentDirectory);
        Assert.Single(services.Settings.DirectoryShortcuts.Items);
    }

    [Fact]
    public void Execute_CommittedPath_NavigatesRenderedPathWhenSettingsChanged()
    {
        var services = CreateServices(out _, out _);
        services.Settings.DirectoryShortcuts.Items.Add(Item(1, _otherTarget));

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left));
        services.WaitForPanelLoads();

        Assert.Equal(_target, services.PanelWorkspace.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void Execute_CommittedPath_NavigatesRenderedPathWhenShortcutWasDeleted()
    {
        var services = CreateServices(out _, out _);

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left));
        services.WaitForPanelLoads();

        Assert.Equal(_target, services.PanelWorkspace.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void Execute_CommittedInvocationIgnoresHiddenWorkspaceMode()
    {
        var services = CreateServices(out _, out _);
        string passiveDirectory = services.PanelWorkspace.PassiveState.CurrentDirectory;
        services.WorkspaceModeController.TogglePanels();

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left));
        services.WaitForPanelLoads();

        Assert.Equal(_target, services.Session.Panels.Left.CurrentDirectory);
        Assert.Equal(passiveDirectory, services.Session.Panels.Right.CurrentDirectory);
    }

    [Fact]
    public void Execute_CommittedInvocationUsesCommittedSideWhenLiveSideChanged()
    {
        var services = CreateServices(out _, out _);
        string rightDirectory = services.Session.Panels.Right.CurrentDirectory;
        services.PanelWorkspace.ActiveSide = PanelSide.Right;

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left));
        services.WaitForPanelLoads();

        Assert.Equal(_target, services.Session.Panels.Left.CurrentDirectory);
        Assert.Equal(rightDirectory, services.Session.Panels.Right.CurrentDirectory);
        Assert.Equal(PanelSide.Right, services.PanelWorkspace.ActiveSide);
    }

    [Fact]
    public void Execute_CurrentSettingsPath_NavigatesLatestPathWhenCommittedPathAbsent()
    {
        var services = CreateServices(out _, out _);
        services.Settings.DirectoryShortcuts.Items.Add(Item(1, _otherTarget));

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToDirectoryShortcutArgs(1));
        services.WaitForPanelLoads();

        Assert.Equal(_otherTarget, services.PanelWorkspace.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void CanExecute_DemoPanel_IsUnavailable()
    {
        var services = CreateServices(out _, out _);
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Demo("/");

        Assert.True(services.CommandRegistry.TryGetCommand(DirectoryShortcutCommandIds.Navigate, out var command));

        Assert.False(command.CanExecute(new NavigateToDirectoryShortcutArgs(1)));
        Assert.False(command.CanExecute(new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left)));
    }

    [Fact]
    public void Execute_CommittedPath_DemoPanel_DoesNotNavigate()
    {
        var services = CreateServices(out _, out _);
        services.Session.Panels.Left.CurrentLocation = PanelLocation.Demo("/");

        services.CommandRegistry.Execute(DirectoryShortcutCommandIds.Navigate, new NavigateToCommittedDirectoryShortcutArgs(1, _target, PanelSide.Left));
        services.WaitForPanelLoads();

        Assert.Equal("/", services.Session.Panels.Left.CurrentDirectory);
        Assert.Equal(PanelSourceId.Demo, services.Session.Panels.Left.SourceId);
    }

    private ApplicationServices CreateServices(
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
        fs.AddDirectory(_otherTarget);
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
        return services;
    }

    private static AppSettings.DirectoryShortcutItem Item(int number, string path) =>
        new()
        {
            Number = number,
            Name = "Target",
            Path = path,
        };
}
