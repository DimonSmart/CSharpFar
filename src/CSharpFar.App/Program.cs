using CSharpFar.App;
using CSharpFar.App.History;
using CSharpFar.App.Settings;
using CSharpFar.Console;
using CSharpFar.FileSystem;
using CSharpFar.Shell;

var settingsStore = JsonSettingsStore.Create();
var settings      = settingsStore.Settings;

using var driver = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);

var fs      = new FileSystemService(settings.Ui.ShowHiddenFiles, settings.Ui.ShowSystemFiles);
var fileOps = new FileOperationService();
var shell   = new ShellService(settings.Shell.Executable, settings.Shell.ArgumentsFormat);

var historyPath = Path.Combine(settingsStore.ConfigDirectory, "history.json");
var history = new JsonHistoryStore(
    historyPath,
    settings.History.MaxCommandHistoryItems,
    settings.History.MaxDirectoryHistoryItems,
    settings.History.MaxFileHistoryItems);

new Application(renderer, fs, shell, fileOps, history, settings).Run();
