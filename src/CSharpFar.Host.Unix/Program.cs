using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.Console.Ansi;
using CSharpFar.Platform.Unix;
using System.Reflection;

var settingsStore = JsonSettingsStore.Create(
    createDefaultSettings: UnixPlatformServices.CreateDefaultSettings);

ValidateShellSettings(settingsStore);

if (args is ["--version"])
{
    PrintVersion();
    return 0;
}

if (args is ["--self-test"])
    return RunSelfTest(settingsStore);

if (args is ["--check-terminal"])
    return RunTerminalCheck();

using var platform = UnixPlatformServices.Create(
    settingsStore.ConfigDirectory,
    settingsStore.Settings.Shell);

ApplicationBootstrap.Run(platform.ConsoleDriver, platform, settingsStore);
return 0;

static void PrintVersion()
{
    var assembly = typeof(Program).Assembly;
    string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                     assembly.GetName().Version?.ToString() ??
                     "unknown";
    Console.WriteLine($"CSharpFar {version}");
}

static int RunSelfTest(JsonSettingsStore settingsStore)
{
    if (OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Unix host cannot run on Windows.");
        return 1;
    }

    Directory.CreateDirectory(settingsStore.ConfigDirectory);
    if (!Directory.Exists(settingsStore.ConfigDirectory))
    {
        Console.Error.WriteLine($"Config directory is unavailable: {settingsStore.ConfigDirectory}");
        return 1;
    }

    if (!IsCommandAvailable(settingsStore.Settings.Shell.Executable))
    {
        Console.Error.WriteLine($"Shell executable is unavailable: {settingsStore.Settings.Shell.Executable}");
        return 1;
    }

    Console.WriteLine("CSharpFar self-test passed.");
    return 0;
}

static int RunTerminalCheck()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal.");
        return 0;
    }

    try
    {
        using var driver = new AnsiTerminalConsoleDriver();
        Console.WriteLine("Terminal check passed.");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static void ValidateShellSettings(JsonSettingsStore settingsStore)
{
    var shell = settingsStore.Settings.Shell;
    if (IsWindowsShellName(shell.Executable) && !IsCommandAvailable(shell.Executable))
    {
        shell.Executable = "/bin/sh";
        shell.ArgumentsFormat = "-c";
        settingsStore.Save();
    }
}

static bool IsWindowsShellName(string executable)
{
    string name = Path.GetFileName(executable);
    return string.Equals(name, "cmd.exe", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, "powershell.exe", StringComparison.OrdinalIgnoreCase);
}

static bool IsCommandAvailable(string executable)
{
    if (Path.IsPathRooted(executable))
        return File.Exists(executable);

    string? path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
        return false;

    return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, executable)));
}
