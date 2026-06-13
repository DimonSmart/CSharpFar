using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.History;
using CSharpFar.App.Settings;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.FileSystem;
using CSharpFar.Core.Services;
using CSharpFar.Shell;

var settingsStore = JsonSettingsStore.Create();
var settings      = settingsStore.Settings;

using var driver = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);

var fs          = new FileSystemService();
var panelSources = new FilePanelSourceRegistry([new LocalFilePanelSource(fs)]);
var fileOps     = new FileOperationService(panelSources);
var searchSvc   = new FileSystemSearchService();
var shell       = new ShellService(settings.Shell.Executable, settings.Shell.ArgumentsFormat);
var fileLauncher = new WindowsShellFileLauncher();
var userMenu    = new UserMenuStore(settingsStore.ConfigDirectory);
var credentials = new DpapiCredentialStore(settingsStore.ConfigDirectory);
var volumeSvc      = new WindowsVolumeService();
var volumeInfoSvc  = new VolumeInfoService();
var locationSvc    = new FileSystemLocationService();
var mountPointSvc  = new VolumeMountPointService();
using var changeWatcher = new FileSystemChangeWatcher();

var historyPath = Path.Combine(settingsStore.ConfigDirectory, "history.json");
var history = new JsonHistoryStore(
    historyPath,
    settings.History.MaxCommandHistoryItems,
    settings.History.MaxDirectoryHistoryItems,
    settings.History.MaxFileHistoryItems);

ApplicationFactory.Create(renderer, fs, shell, fileOps, history, settings, userMenu,
    saveSettings:     () => settingsStore.Save(),
    volumeService:    volumeSvc,
    volumeInfoService: volumeInfoSvc,
    changeWatcher:     changeWatcher,
    locationService:   locationSvc,
    mountPointService: mountPointSvc,
    fileLauncher:      fileLauncher,
    searchService:     searchSvc,
    sourceRegistry:    panelSources,
    credentialStore:   credentials,
    configDirectory:   settingsStore.ConfigDirectory,
    terminalScreenMode: driver).Run();
