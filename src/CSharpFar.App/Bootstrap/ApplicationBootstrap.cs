using CSharpFar.App.History;
using CSharpFar.App.Settings;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.App.Bootstrap;

public static class ApplicationBootstrap
{
    public static void Run(
        IConsoleDriver driver,
        IPlatformServices platform,
        JsonSettingsStore settingsStore,
        ApplicationRunOptions? runOptions = null)
    {
        runOptions ??= ApplicationRunOptions.Normal;
        var settings = settingsStore.Settings;
        var renderer = new ScreenRenderer(driver);

        if (runOptions.Mode == ApplicationRunMode.Demo)
        {
            RunDemo(renderer, platform, settingsStore, runOptions, settings);
            return;
        }

        RunNormal(renderer, platform, settingsStore, settings);
    }

    private static void RunNormal(
        ScreenRenderer renderer,
        IPlatformServices platform,
        JsonSettingsStore settingsStore,
        Core.Models.AppSettings settings)
    {
        var fs = new FileSystemService();
        var panelSources = new FilePanelSourceRegistry([new LocalFilePanelSource(fs)]);
        var fileOps = new FileOperationService(panelSources, platform.FileSystemOperations);
        var searchService = new FileSystemSearchService();
        var userMenu = new UserMenuStore(settingsStore.ConfigDirectory);

        using var changeWatcher = new FileSystemChangeWatcher();

        var historyPath = Path.Combine(settingsStore.ConfigDirectory, "history.json");
        var history = new JsonHistoryStore(
            historyPath,
            settings.History.MaxCommandHistoryItems,
            settings.History.MaxDirectoryHistoryItems,
            settings.History.MaxFileHistoryItems);

        ApplicationFactory.Create(
                renderer,
                fs,
                platform.ShellService,
                fileOps,
                history,
                settings,
                userMenu,
                saveSettings: () => settingsStore.Save(),
                volumeService: platform.VolumeService,
                volumeInfoService: platform.VolumeInfoService,
                changeWatcher: changeWatcher,
                locationService: platform.LocationService,
                mountPointService: platform.VolumeMountPointService,
                fileLauncher: platform.FileLauncher,
                searchService: searchService,
                sourceRegistry: panelSources,
                credentialStore: platform.CredentialStore,
                configDirectory: settingsStore.ConfigDirectory,
                terminalScreenMode: platform.TerminalScreenMode)
            .Run();
    }

    private static void RunDemo(
        ScreenRenderer renderer,
        IPlatformServices platform,
        JsonSettingsStore settingsStore,
        ApplicationRunOptions runOptions,
        Core.Models.AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(runOptions.DemoRootPath))
            throw new InvalidOperationException("Demo mode requires a fixture directory path.");

        var fs = new FileSystemService();
        var demoSource = DemoFilePanelSource.ImportFromDirectory(runOptions.DemoRootPath);
        var panelSources = new FilePanelSourceRegistry([demoSource]);
        var fileOps = new FileOperationService(panelSources, platform.FileSystemOperations);
        var history = new Core.History.InMemoryHistoryStore(
            settings.History.MaxCommandHistoryItems,
            settings.History.MaxDirectoryHistoryItems,
            settings.History.MaxFileHistoryItems);
        string tempConfigDirectory = Path.Combine(Path.GetTempPath(), "CSharpFar.Demo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigDirectory);

        try
        {
            ApplicationFactory.Create(
                    renderer,
                    fs,
                    new DemoModeServices.DisabledShellService(),
                    fileOps,
                    history,
                    settings,
                    new UserMenuStore(tempConfigDirectory),
                    saveSettings: null,
                    volumeService: new DemoModeServices.DemoVolumeService(),
                    volumeInfoService: null,
                    changeWatcher: null,
                    locationService: null,
                    mountPointService: null,
                    fileLauncher: new DemoModeServices.DisabledFileLauncher(),
                    searchService: new FileSystemSearchService(),
                    sourceRegistry: panelSources,
                    credentialStore: platform.CredentialStore,
                    enableBuiltInNetworkModules: false,
                    configDirectory: tempConfigDirectory,
                    terminalScreenMode: platform.TerminalScreenMode,
                    runOptions: runOptions)
                .Run();
        }
        finally
        {
            try
            {
                Directory.Delete(tempConfigDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
