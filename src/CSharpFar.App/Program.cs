using CSharpFar.App;
using CSharpFar.App.History;
using CSharpFar.Console;
using CSharpFar.FileSystem;
using CSharpFar.Shell;

using var driver = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);
var fs       = new FileSystemService();
var fileOps  = new FileOperationService();
var shell    = new ShellService();
var history  = new JsonHistoryStore();

new Application(renderer, fs, shell, fileOps, history).Run();
