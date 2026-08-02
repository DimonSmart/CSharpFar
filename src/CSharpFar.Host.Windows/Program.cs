using System.Reflection;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Settings;
using CSharpFar.Platform.Windows;

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
        ApplicationCrashReport.Write(exception);
};

try
{
    if (args is ["--version"])
    {
        PrintVersion();
        return 0;
    }

    if (args is ["--self-test"])
    {
        using var selfTestStartup = ApplicationStartupContext.Create(
            ApplicationRunOptions.Normal,
            WindowsPlatformServices.CreateDefaultSettings,
            ValidateShellSettings);
        return RunSelfTest(selfTestStartup.SettingsStore);
    }

    if (args is ["--check-terminal"])
    {
        using var terminalCheckStartup = ApplicationStartupContext.Create(
            ApplicationRunOptions.Normal,
            WindowsPlatformServices.CreateDefaultSettings,
            ValidateShellSettings);
        return RunTerminalCheck(terminalCheckStartup.SettingsStore);
    }

    if (!ApplicationRunOptionsParser.TryParse(args, out var runOptions, out string? runError))
    {
        Console.Error.WriteLine(runError);
        return 2;
    }

    if (!ApplicationRunOptionsValidator.TryValidate(runOptions, out string? validationError))
    {
        Console.Error.WriteLine(validationError);
        return 1;
    }

    using var startup = ApplicationStartupContext.Create(
        runOptions,
        WindowsPlatformServices.CreateDefaultSettings,
        ValidateShellSettings);
    JsonSettingsStore settingsStore = startup.SettingsStore;

    using var platform = WindowsPlatformServices.Create(
        settingsStore.ConfigDirectory,
        settingsStore.Settings.Shell);

    try
    {
        ApplicationBootstrap.Run(platform.ConsoleDriver, platform, settingsStore, runOptions);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
catch (Exception ex)
{
    string? reportPath = ApplicationCrashReport.Write(ex);
    Console.Error.WriteLine(reportPath is null
        ? ex.ToString()
        : $"CSharpFar stopped because of an unexpected error. Details were saved to: {reportPath}");
    return 1;
}

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

static int RunTerminalCheck(JsonSettingsStore settingsStore)
{
    try
    {
        using var platform = WindowsPlatformServices.Create(settingsStore.ConfigDirectory, settingsStore.Settings.Shell);
        Console.WriteLine($"Terminal check passed. Supported={platform.TerminalScreenMode.IsSupported}");
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
    if (string.Equals(shell.Executable, "/bin/sh", StringComparison.OrdinalIgnoreCase) && !IsCommandAvailable(shell.Executable))
    {
        shell.Executable = "cmd.exe";
        shell.ArgumentsFormat = "/c";
        settingsStore.Save();
    }
}

static bool IsCommandAvailable(string executable)
{
    if (Path.IsPathRooted(executable))
        return File.Exists(executable);

    string? path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
        return false;

    string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
        .Split(';', StringSplitOptions.RemoveEmptyEntries);

    foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        if (File.Exists(Path.Combine(directory, executable)))
            return true;
        foreach (string extension in extensions)
        {
            if (File.Exists(Path.Combine(directory, executable + extension)))
                return true;
        }
    }

    return false;
}
