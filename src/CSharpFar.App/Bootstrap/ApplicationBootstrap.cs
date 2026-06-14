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
        JsonSettingsStore settingsStore)
    {
        var settings = settingsStore.Settings;
        var renderer = new ScreenRenderer(driver);

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
}
