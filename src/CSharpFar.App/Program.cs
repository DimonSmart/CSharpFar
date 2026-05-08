using CSharpFar.App;
using CSharpFar.App.History;
using CSharpFar.App.Settings;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.FileSystem;
using CSharpFar.Shell;

var settingsStore = JsonSettingsStore.Create();
var settings      = settingsStore.Settings;

using var driver = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);

var fs          = new FileSystemService();
var fileOps     = new FileOperationService();
var shell       = new ShellService(settings.Shell.Executable, settings.Shell.ArgumentsFormat);
var fileLauncher = new WindowsShellFileLauncher();
var userMenu    = new UserMenuStore(settingsStore.ConfigDirectory);
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

new Application(renderer, fs, shell, fileOps, history, settings, userMenu,
    saveSettings:     () => settingsStore.Save(),
    volumeService:    volumeSvc,
    volumeInfoService: volumeInfoSvc,
    changeWatcher:     changeWatcher,
    locationService:   locationSvc,
    mountPointService: mountPointSvc,
    fileLauncher:      fileLauncher).Run();
