using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.Console.Ansi;
using CSharpFar.Console.Input;
using CSharpFar.Platform.Unix;
using System.Reflection;
using System.Runtime.InteropServices;

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
    return RunTerminalCheck(inputMode: false);

if (args is ["--check-terminal", "--input"])
    return RunTerminalCheck(inputMode: true);

if (args is ["--check-terminal", "--raw-input"])
    return RunRawTerminalInputCheck();

if (args is ["--check-terminal", "--mouse-input"])
    return RunMouseTerminalInputCheck();

if (args is ["--check-terminal", "--enhanced-input"] or ["--check-terminal", "--kitty-input"])
    return RunEnhancedTerminalInputCheck();

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

static int RunTerminalCheck(bool inputMode)
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal.");
        return 0;
    }

    try
    {
        using var driver = new AnsiTerminalConsoleDriver();
        if (inputMode)
            RunTerminalInputCheck(driver);
        else
            RunTerminalVisualCheck(driver);
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int RunRawTerminalInputCheck()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal.");
        return 0;
    }

    try
    {
        using var driver = new AnsiTerminalConsoleDriver();
        RunTerminalRawInputCheck(driver);
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int RunEnhancedTerminalInputCheck()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal.");
        return 0;
    }

    try
    {
        using var driver = new AnsiTerminalConsoleDriver();
        RunTerminalEnhancedInputCheck(driver);
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int RunMouseTerminalInputCheck()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.WriteLine("Terminal check skipped: stdin/stdout are not attached to a terminal.");
        return 0;
    }

    try
    {
        using var driver = new AnsiTerminalConsoleDriver();
        RunTerminalMouseInputCheck(driver);
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

static void RunTerminalInputCheck(AnsiTerminalConsoleDriver driver)
{
    driver.EnterApplicationScreen();
    driver.Clear();
    driver.SetCursorVisible(false);
    driver.WriteAt(2, 1, "Input check. Press Esc or Ctrl+C to exit.", ConsoleColor.White, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Bold);

    int row = 3;
    while (true)
    {
        var key = driver.ReadKey(intercept: true);
        driver.WriteAt(2, row, new string(' ', Math.Max(1, driver.GetBufferWidth() - 4)), ConsoleColor.Gray, ConsoleColor.Black);
        driver.WriteAt(2, row, $"Key: {key.Key}, Modifiers: {key.Modifiers}", ConsoleColor.Yellow, ConsoleColor.Black);
        row++;
        if (row >= Math.Max(4, driver.GetBufferHeight() - 1))
        {
            row = 3;
            driver.ClearRegion(new CSharpFar.Console.Models.Rect(0, 3, driver.GetBufferWidth(), Math.Max(1, driver.GetBufferHeight() - 3)));
        }

        if (key.Key == ConsoleKey.Escape ||
            (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            break;
        }
    }

    driver.RestoreTerminal();
}

static void RunTerminalRawInputCheck(AnsiTerminalConsoleDriver driver)
{
    driver.EnterApplicationScreen();
    driver.Clear();
    driver.SetCursorVisible(false);
    driver.WriteAt(2, 1, "Raw input check. Press Esc or Ctrl+C to exit.", ConsoleColor.White, ConsoleColor.DarkBlue, CSharpFar.Console.Models.TextAttributes.Bold);

    int row = 3;
    while (true)
    {
        var result = driver.ReadRawInput();
        var key = result.Key;
        driver.WriteAt(2, row, new string(' ', Math.Max(1, driver.GetBufferWidth() - 4)), ConsoleColor.Gray, ConsoleColor.Black);
        driver.WriteAt(2, row, $"bytes: {FormatBytes(result.Bytes)}", ConsoleColor.Gray, ConsoleColor.Black);
        row = NextRawInputRow(driver, row);
        driver.WriteAt(2, row, new string(' ', Math.Max(1, driver.GetBufferWidth() - 4)), ConsoleColor.Gray, ConsoleColor.Black);
        driver.WriteAt(2, row, $"text : {FormatInputText(result.Bytes)}", ConsoleColor.Gray, ConsoleColor.Black);
        row = NextRawInputRow(driver, row);
        driver.WriteAt(2, row, new string(' ', Math.Max(1, driver.GetBufferWidth() - 4)), ConsoleColor.Gray, ConsoleColor.Black);
        driver.WriteAt(2, row, $"parsed: {key.Key}, Modifiers: {key.Modifiers}", ConsoleColor.Yellow, ConsoleColor.Black);
        Console.Out.Flush();
        row = NextRawInputRow(driver, row);

        if (key.Key == ConsoleKey.Escape ||
            (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            break;
        }
    }

    driver.RestoreTerminal();
}

static void RunTerminalEnhancedInputCheck(AnsiTerminalConsoleDriver driver)
{
    const string Csi = "\x1b[";
    const int RequestedFlags = 11;

    driver.EnterApplicationScreen();
    driver.Clear();
    driver.SetCursorVisible(false);
    driver.EnableRawInputMode();

    try
    {
        Console.WriteLine("Enhanced terminal input check");
        Console.WriteLine("This is an experiment. It does not represent production Unix input behavior.");
        Console.WriteLine("Press Esc or Ctrl+C to exit.");
        Console.WriteLine();
        PrintManualEnhancedInputChecklist();
        Console.WriteLine();
        Console.WriteLine("Terminal protocol handshake:");

        int? before = SendEnhancedInputQuery(driver, $"{Csi}?u", expectResponse: true);
        SendEnhancedInputQuery(driver, $"{Csi}>u", expectResponse: false);
        SendEnhancedInputQuery(driver, $"{Csi}=11;1u", expectResponse: false);
        int? after = SendEnhancedInputQuery(driver, $"{Csi}?u", expectResponse: true);

        int? reportedFlags = after ?? before;
        string support = reportedFlags.HasValue
            ? (reportedFlags.Value & RequestedFlags) == RequestedFlags ? "yes" : "no"
            : "unknown";
        Console.WriteLine($"  support: {support}");
        Console.WriteLine($"  requested flags: {RequestedFlags}");
        Console.WriteLine($"  reported flags: {FormatNullableNumber(reportedFlags)}");
        if (!reportedFlags.HasValue)
        {
            Console.WriteLine("Protocol support: unknown or not supported");
            Console.WriteLine("Continuing raw input capture without confirmed enhanced mode.");
        }

        Console.WriteLine();

        while (true)
        {
            var result = driver.ReadRawInput();
            var enhanced = EnhancedTerminalKeyParser.Parse(result.Bytes);
            PrintEnhancedInputEvent(result, enhanced);
            Console.Out.Flush();

            if (IsEnhancedInputExit(result.Key, enhanced))
                break;
        }
    }
    finally
    {
        driver.WriteRawControl($"{Csi}<u");
        driver.RestoreTerminal();
    }
}

static void RunTerminalMouseInputCheck(AnsiTerminalConsoleDriver driver)
{
    const string EnableMouse = "\x1b[?1002h\x1b[?1006h";
    const string DisableMouse = "\x1b[?1002l\x1b[?1006l";
    string logPath = Path.Combine(
        Path.GetTempPath(),
        $"csharpfar-mouse-input-check-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
    driver.EnterApplicationScreen();
    driver.Clear();
    driver.SetCursorVisible(false);
    driver.EnableRawInputMode();

    try
    {
        driver.WriteRawControl(EnableMouse);
        PrintMouseInputHeader(log, logPath);

        int eventNumber = 0;
        var lastPressedButton = MouseButton.Left;
        var lastSize = driver.GetSize();
        while (true)
        {
            var size = driver.GetSize();
            if (size.Width != lastSize.Width || size.Height != lastSize.Height)
            {
                lastSize = size;
                PrintDiagnosticEvent(
                    log,
                    ++eventNumber,
                    [$"resize: width={size.Width} height={size.Height}"]);
            }

            if (!driver.TryReadRawInput(100, out var input))
                continue;

            if (SgrMouseInputParser.TryParse(
                    input.Bytes,
                    ref lastPressedButton,
                    out var mouseResult,
                    out string? parseError))
            {
                var mouse = mouseResult.Mouse;
                PrintDiagnosticEvent(
                    log,
                    ++eventNumber,
                    [
                        $"bytes : {FormatBytes(input.Bytes)}",
                        $"text  : {FormatInputText(input.Bytes)}",
                        $"mouse : {mouse.Kind} {mouse.Button} x={mouse.X} y={mouse.Y} " +
                        $"mods={FormatMouseModifiers(mouse.Modifiers)} cb={mouseResult.EncodedButton} final={mouseResult.Final}",
                    ]);
            }
            else
            {
                var lines = new List<string>
                {
                    $"bytes : {FormatBytes(input.Bytes)}",
                    $"text  : {FormatInputText(input.Bytes)}",
                    $"key   : {input.Key.Key} mods={FormatConsoleModifiers(input.Key.Modifiers)}",
                };
                if (parseError is not null)
                    lines.Add($"mouse parse error: {parseError}");

                PrintDiagnosticEvent(log, ++eventNumber, lines);
            }

            Console.Out.Flush();
            if (input.Key.Key == ConsoleKey.Escape ||
                (input.Key.Key == ConsoleKey.C && input.Key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                break;
            }
        }
    }
    finally
    {
        driver.WriteRawControl(DisableMouse);
        driver.RestoreTerminal();
    }
}

static void PrintMouseInputHeader(StreamWriter log, string logPath)
{
    string[] environmentVariables =
    [
        "TERM",
        "COLORTERM",
        "WT_SESSION",
        "WSL_DISTRO_NAME",
        "SSH_TTY",
        "TTY",
    ];

    var lines = new List<string>
    {
        "CSharpFar Unix mouse input check",
        "This is an experiment. It does not represent production Unix input behavior.",
        "Press Esc or Ctrl+C to exit.",
        "",
        $"Log file: {logPath}",
        $"Date/time: {DateTimeOffset.Now:O}",
        $"OS: {RuntimeInformation.OSDescription}",
        $"Process architecture: {RuntimeInformation.ProcessArchitecture}",
        "",
        "Environment:",
    };
    lines.AddRange(environmentVariables.Select(
        name => $"  {name}={Environment.GetEnvironmentVariable(name) ?? "<unset>"}"));
    lines.AddRange(
    [
        "",
        "Mouse mode:",
        "  enabled: ESC[?1002h ESC[?1006h",
        "  disabled on exit: ESC[?1002l ESC[?1006l",
        "",
        "Manual checklist:",
        "  1. Type abc",
        "  2. Press arrows, Enter, Tab, Backspace, Esc",
        "  3. Click left/right/middle",
        "  4. Double click left",
        "  5. Drag with left button",
        "  6. Use mouse wheel up/down",
        "  7. Shift+click, Ctrl+click, Alt+click",
        "  8. Resize terminal window",
        "",
    ]);

    foreach (string line in lines)
    {
        Console.WriteLine(line);
        log.WriteLine(line);
    }
}

static void PrintDiagnosticEvent(StreamWriter log, int eventNumber, IEnumerable<string> lines)
{
    Console.WriteLine($"#{eventNumber}");
    log.WriteLine($"#{eventNumber}");
    foreach (string line in lines)
    {
        Console.WriteLine(line);
        log.WriteLine(line);
    }

    Console.WriteLine();
    log.WriteLine();
}

static string FormatMouseModifiers(MouseKeyModifiers modifiers) =>
    modifiers == MouseKeyModifiers.None ? "None" : modifiers.ToString();

static string FormatConsoleModifiers(ConsoleModifiers modifiers) =>
    modifiers == 0 ? "None" : modifiers.ToString();

static int? SendEnhancedInputQuery(
    AnsiTerminalConsoleDriver driver,
    string sequence,
    bool expectResponse)
{
    Console.WriteLine($"  sent: {FormatSentControl(sequence)}");
    driver.WriteRawControl(sequence);
    if (!expectResponse)
        return null;

    if (!driver.TryReadRawInput(200, out var response))
    {
        Console.WriteLine("  recv: <timeout>");
        return null;
    }

    Console.WriteLine($"  recv: {FormatInputText(response.Bytes)}");
    return TryParseEnhancedKeyboardFlags(response.Bytes, out int flags) ? flags : null;
}

static void PrintManualEnhancedInputChecklist()
{
    Console.WriteLine("Manual test checklist:");
    Console.WriteLine();
    Console.WriteLine("1. Press and release Ctrl alone.");
    Console.WriteLine("2. Press and release Shift alone.");
    Console.WriteLine("3. Press and release Alt alone.");
    Console.WriteLine("4. Press Ctrl+Right.");
    Console.WriteLine("5. Press Shift+F5.");
    Console.WriteLine("6. Press Alt+Left.");
    Console.WriteLine("7. Press Ctrl+C.");
    Console.WriteLine("8. Press Esc.");
    Console.WriteLine("9. Type plain text: abc.");
    Console.WriteLine("10. Press Enter, Tab, Backspace.");
}

static void PrintEnhancedInputEvent(AnsiInputReadResult result, EnhancedTerminalKeyEvent enhanced)
{
    Console.WriteLine($"bytes   : {FormatBytes(result.Bytes)}");
    Console.WriteLine($"text    : {FormatInputText(result.Bytes)}");
    if (enhanced.IsKnown)
    {
        Console.WriteLine($"event   : keyCode={enhanced.KeyCode}, modifiersRaw={enhanced.ModifiersRaw}, eventType={enhanced.EventType}");
        Console.WriteLine(
            "mods    : " +
            $"Shift={enhanced.Modifiers.HasFlag(EnhancedModifiers.Shift)}, " +
            $"Alt={enhanced.Modifiers.HasFlag(EnhancedModifiers.Alt)}, " +
            $"Ctrl={enhanced.Modifiers.HasFlag(EnhancedModifiers.Ctrl)}, " +
            $"Super={enhanced.Modifiers.HasFlag(EnhancedModifiers.Super)}, " +
            $"Hyper={enhanced.Modifiers.HasFlag(EnhancedModifiers.Hyper)}, " +
            $"Meta={enhanced.Modifiers.HasFlag(EnhancedModifiers.Meta)}");
        Console.WriteLine($"parsed  : ConsoleKey={enhanced.ParsedKey.Key}, ConsoleModifiers={enhanced.ParsedKey.Modifiers}");
        Console.WriteLine($"modifier-only: {enhanced.ModifierOnly.ToString().ToLowerInvariant()}");
        if (enhanced.ModifierOnly)
            Console.WriteLine($"{FormatModifierName(enhanced.ModifierKeyName)} {enhanced.EventType.ToString().ToLowerInvariant()}");
    }
    else
    {
        Console.WriteLine("parsed: UnknownEnhancedSequence");
        Console.WriteLine($"fallback: ConsoleKey={result.Key.Key}, ConsoleModifiers={result.Key.Modifiers}");
    }

    Console.WriteLine();
}

static bool IsEnhancedInputExit(ConsoleKeyInfo fallbackKey, EnhancedTerminalKeyEvent enhanced)
{
    var key = enhanced.IsKnown ? enhanced.ParsedKey : fallbackKey;
    return key.Key == ConsoleKey.Escape ||
        (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control));
}

static int NextRawInputRow(AnsiTerminalConsoleDriver driver, int row)
{
    row++;
    if (row < Math.Max(4, driver.GetBufferHeight() - 1))
        return row;

    driver.ClearRegion(new CSharpFar.Console.Models.Rect(0, 3, driver.GetBufferWidth(), Math.Max(1, driver.GetBufferHeight() - 3)));
    return 3;
}

static string FormatBytes(IReadOnlyList<byte> bytes) =>
    string.Join(' ', bytes.Select(static b => b.ToString("X2")));

static string FormatInputText(IReadOnlyList<byte> bytes)
{
    var parts = bytes.Select(static b => b switch
    {
        0x1b => "ESC",
        0x09 => "TAB",
        0x0d => "CR",
        0x0a => "LF",
        0x7f => "DEL",
        >= 0x20 and <= 0x7e => ((char)b).ToString(),
        _ => $"0x{b:X2}",
    });

    return string.Join(' ', parts);
}

static string FormatSentControl(string sequence) =>
    sequence.Replace("\x1b", "ESC", StringComparison.Ordinal);

static string FormatNullableNumber(int? value) =>
    value.HasValue ? value.Value.ToString() : "unknown";

static string FormatModifierName(string? modifierName)
{
    if (modifierName is null)
        return "MODIFIER";

    if (modifierName.StartsWith("LEFT_", StringComparison.Ordinal))
        return "LEFT_" + modifierName[5..];
    if (modifierName.StartsWith("RIGHT_", StringComparison.Ordinal))
        return "RIGHT_" + modifierName[6..];

    return modifierName;
}

static bool TryParseEnhancedKeyboardFlags(IReadOnlyList<byte> bytes, out int flags)
{
    flags = 0;
    string text = new(bytes.Select(static b => b <= 0x7f ? (char)b : '\ufffd').ToArray());
    const string prefix = "\x1b[?";
    if (!text.StartsWith(prefix, StringComparison.Ordinal) ||
        !text.EndsWith('u') ||
        text.Length <= prefix.Length + 1)
    {
        return false;
    }

    return int.TryParse(text[prefix.Length..^1], out flags);
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
