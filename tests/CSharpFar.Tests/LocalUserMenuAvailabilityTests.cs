using System.Text.Json;
using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.Console;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Testing;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class LocalUserMenuAvailabilityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarLocalMenu_").FullName;
    private string PanelDirectory => Path.Combine(_root, "panel");

    [Fact]
    public void MissingLocalMenuFile_MenuIsEnabledAndFirstItemCanBeSaved()
    {
        string rightDirectory = Path.Combine(_root, "rightPanel");
        var driver = new FakeConsoleDriver();
        ApplicationServices services = CreateServices(driver, PanelDirectory, rightDirectory);
        string menuPath = Path.Combine(PanelDirectory, ".csharpfar-menu.json");

        Assert.False(File.Exists(menuPath));
        services.TopMenu.Open("Options");
        MenuBarDefinition menu = services.RenderContext.BuildMenuDefinition();
        MenuItemDefinition localMenuItem = menu.Items
            .Single(item => item.Id == "Options")
            .Children
            .Single(item => item.Id == "Options.localUserMenu");
        Assert.True(services.CommandRegistry.CanExecute(
            MenuCommandIds.SettingsOpenLocalUserMenu,
            services.CommandContext));
        Assert.True(localMenuItem.IsEnabled);

        driver.EnqueueKey(Key(ConsoleKey.A, 'a'));
        EnqueueText(driver, "First command");
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = services.CommandRegistry.Execute(
            MenuCommandIds.SettingsOpenLocalUserMenu,
            services.CommandContext);

        Assert.True(result.ShouldRender);
        using JsonDocument savedMenu = JsonDocument.Parse(File.ReadAllText(menuPath));
        Assert.Equal("First command", savedMenu.RootElement[0].GetProperty("title").GetString());
        Assert.True(LocalMenuItem(services).IsEnabled);
    }

    [Fact]
    public void AvailabilityTracksActivePanelAndRejectsVirtualSearchErrorAndMissingDirectory()
    {
        string rightDirectory = Path.Combine(_root, "rightPanel");
        var services = CreateServices(new FakeConsoleDriver(), PanelDirectory, rightDirectory);
        FilePanelState rightPanel = services.CommandContext.RightPanel;

        AssertAvailability(services, expected: true);

        services.CommandContext.ActiveSide = PanelSide.Right;
        AssertAvailability(services, expected: true);

        rightPanel.CurrentLocation = new PanelLocation(new PanelSourceId("archive"), rightDirectory);
        rightPanel.ContentKind = PanelContentKind.Virtual;
        AssertAvailability(services, expected: false);

        rightPanel.CurrentLocation = PanelLocation.Local(rightDirectory);
        rightPanel.ContentKind = PanelContentKind.Source;
        rightPanel.SearchRequest = new SearchRequest
        {
            RootPath = rightDirectory,
            FileMaskExpression = "*",
            Scope = SearchScope.CurrentDirectoryOnly,
            MaxDegreeOfParallelism = 1,
        };
        AssertAvailability(services, expected: false);

        rightPanel.SearchRequest = null;
        rightPanel.LoadError = new PanelLoadError
        {
            Message = "Directory load failed.",
            RetryLocation = PanelLocation.Local(rightDirectory),
        };
        AssertAvailability(services, expected: false);

        rightPanel.LoadError = null;
        rightPanel.CurrentLocation = PanelLocation.Local(Path.Combine(_root, "missing"));
        AssertAvailability(services, expected: false);

        rightPanel.CurrentLocation = PanelLocation.Local(rightDirectory);
        AssertAvailability(services, expected: true);

        services.CommandContext.ActiveSide = PanelSide.Left;
        AssertAvailability(services, expected: true);
    }

    [Fact]
    public void LocalUserMenuRemainsUnavailableInDemoMode()
    {
        string fixture = Path.Combine(_root, "demoFixture");
        string configDirectory = Path.Combine(_root, "demoConfig");
        Directory.CreateDirectory(fixture);
        var settings = new AppSettings();
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            new DemoModeServices.DisabledLocalFileSystemService(),
            new DemoModeServices.DisabledShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            sourceRegistry: new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]),
            configDirectory: configDirectory,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixture));
        _ = new Application(services);

        AssertAvailability(services, expected: false);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char character in text)
        {
            ConsoleKey key = character == ' '
                ? ConsoleKey.Spacebar
                : Enum.Parse<ConsoleKey>(character.ToString(), ignoreCase: true);
            driver.EnqueueKey(Key(key, character));
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar = '\0') =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private ApplicationServices CreateServices(
        FakeConsoleDriver driver,
        string leftDirectory,
        string rightDirectory)
    {
        Directory.CreateDirectory(leftDirectory);
        Directory.CreateDirectory(rightDirectory);
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(leftDirectory);
        fileSystem.AddDirectory(rightDirectory);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = leftDirectory;
        settings.Panels.RightStartDirectory = rightDirectory;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fileSystem,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            configDirectory: Path.Combine(_root, "config"));
        _ = new Application(services);
        return services;
    }

    private static MenuItemDefinition LocalMenuItem(ApplicationServices services)
    {
        services.TopMenu.Open("Options");
        return services.RenderContext.BuildMenuDefinition().Items
            .Single(item => item.Id == "Options")
            .Children
            .Single(item => item.Id == "Options.localUserMenu");
    }

    private static void AssertAvailability(ApplicationServices services, bool expected)
    {
        Assert.Equal(expected, LocalMenuItem(services).IsEnabled);
        Assert.Equal(expected, services.CommandRegistry.CanExecute(
            MenuCommandIds.SettingsOpenLocalUserMenu,
            services.CommandContext));
    }
}
