using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.FileSystem;

var driver   = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);
var fs       = new FileSystemService();

new Application(renderer, fs).Run();
