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

if (args.Length >= 2 && args[0] == "--check-terminal" && args[1] == "--input-lab")
{
    if (!TerminalInputLabOptions.TryParse(args.Skip(2), out var options, out string? error))
    {
        Console.Error.WriteLine(error);
        return 2;
    }

    return TerminalInputLab.Run(options);
}

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
        RunTerminalVisualCheck(driver);
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static void RunTerminalVisualCheck(AnsiTerminalConsoleDriver driver)
{
    driver.EnterApplicationScreen();
    driver.SetCursorVisible(false);
    driver.Clear();

    var colors = Enum.GetValues<ConsoleColor>();
    driver.WriteAt(2, 1, "CSharpFar terminal backend check", ConsoleColor.White, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Bold);
    driver.WriteAt(2, 3, "Foreground colors", ConsoleColor.White, ConsoleColor.Black, CSharpFar.Console.Models.TextAttributes.Underline);

    for (int i = 0; i < colors.Length; i++)
    {
        var color = colors[i];
        driver.WriteAt(4, 5 + i, color.ToString().PadRight(12), color, ConsoleColor.Black);
        driver.WriteAt(20, 5 + i, "  " + color + "  ", ConsoleColor.Black, color);
    }

    int boxX = 42;
    int boxY = 5;
    int boxW = 30;
    int boxH = 9;
    driver.WriteAt(boxX, boxY, "+" + new string('-', boxW - 2) + "+", ConsoleColor.Cyan, ConsoleColor.Black);
    for (int y = boxY + 1; y < boxY + boxH - 1; y++)
        driver.WriteAt(boxX, y, "|" + new string(' ', boxW - 2) + "|", ConsoleColor.Cyan, ConsoleColor.DarkBlue);
    driver.WriteAt(boxX, boxY + boxH - 1, "+" + new string('-', boxW - 2) + "+", ConsoleColor.Cyan, ConsoleColor.Black);
    driver.WriteAt(boxX + 2, boxY + 2, "Panel frame", ConsoleColor.Yellow, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Bold);
    driver.WriteAt(boxX + 2, boxY + 4, "normal", ConsoleColor.Gray, ConsoleColor.DarkBlue);
    driver.WriteAt(boxX + 10, boxY + 4, "reverse", ConsoleColor.Gray, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Reverse);
    driver.WriteAt(boxX + 2, boxY + 6, "bold", ConsoleColor.White, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Bold);
    driver.WriteAt(boxX + 10, boxY + 6, "underline", ConsoleColor.White, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Underline);

    int promptY = Math.Max(0, Math.Min(driver.GetBufferHeight() - 2, 23));
    driver.WriteAt(2, promptY, Directory.GetCurrentDirectory() + ">", ConsoleColor.White, ConsoleColor.Black);
    driver.WriteAt(2, promptY + 1, "Press any key", ConsoleColor.Yellow, ConsoleColor.Black);
    driver.SetCursorPosition(Math.Min(driver.GetBufferWidth() - 1, Directory.GetCurrentDirectory().Length + 3), promptY);
    driver.SetCursorVisible(true);
    driver.ReadKey(intercept: true);
    driver.RestoreTerminal();
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
