using System.Reflection;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Settings;
using CSharpFar.Console.Ansi;
using CSharpFar.Platform.MacOs;

try
{
    if (args is ["--version"]) { PrintVersion(); return 0; }
    if (args is ["--self-test"]) { using var startup = ApplicationStartupContext.Create(ApplicationRunOptions.Normal, MacOsPlatformServices.CreateDefaultSettings, ValidateShellSettings); return RunSelfTest(startup.SettingsStore); }
    if (args is ["--check-terminal"] || args is ["--check-terminal", "--input-lab"]) return RunTerminalCheck();
    if (!ApplicationRunOptionsParser.TryParse(args, out var options, out string? error) || !ApplicationRunOptionsValidator.TryValidate(options, out error)) { Console.Error.WriteLine(error); return 2; }
    using var context = ApplicationStartupContext.Create(options, MacOsPlatformServices.CreateDefaultSettings, ValidateShellSettings);
    using var platform = MacOsPlatformServices.Create(context.SettingsStore.ConfigDirectory, context.SettingsStore.Settings.Shell);
    ApplicationBootstrap.Run(platform.ConsoleDriver, platform, context.SettingsStore, options); return 0;
}
catch (Exception ex) { string? report = ApplicationCrashReport.Write(ex); Console.Error.WriteLine(report is null ? ex.ToString() : $"CSharpFar stopped because of an unexpected error. Details were saved to: {report}"); return 1; }

static void PrintVersion() => Console.WriteLine($"CSharpFar {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"}");
static int RunSelfTest(JsonSettingsStore settings)
{
    if (!OperatingSystem.IsMacOS()) { Console.Error.WriteLine("macOS host cannot run on this platform."); return 1; }
    Directory.CreateDirectory(settings.ConfigDirectory);
    if (!File.Exists(settings.Settings.Shell.Executable)) { Console.Error.WriteLine($"Shell executable is unavailable: {settings.Settings.Shell.Executable}"); return 1; }
    Console.WriteLine("CSharpFar self-test passed."); return 0;
}
static int RunTerminalCheck()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected) { Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal."); return 0; }
    using var driver = AnsiTerminalConsoleDriver.CreateMacOs(); driver.EnterApplicationScreen(); driver.SetCursorVisible(false); driver.Clear(); driver.WriteAt(2, 1, "CSharpFar macOS terminal backend check", ConsoleColor.White, ConsoleColor.DarkBlue); driver.RestoreTerminal(); return 0;
}
static void ValidateShellSettings(JsonSettingsStore settings)
{
    if (!File.Exists(settings.Settings.Shell.Executable)) { settings.Settings.Shell.Executable = "/bin/sh"; settings.Settings.Shell.ArgumentsFormat = "-c"; settings.Save(); }
}
