using System.Runtime.InteropServices;
using System.Text.Json;
using CSharpFar.Console.Input;
using SystemConsole = System.Console;

namespace CSharpFar.Console.Ansi;

internal enum TerminalInputLabPlatform
{
    Linux,
    MacOs,
}

internal sealed record TerminalInputLabOptions(
    bool Manual = true,
    bool MouseAllMotion = false,
    bool EnhancedKeyboard = true,
    int EscapeTimeoutMilliseconds = 50,
    int StepSeconds = 5)
{
    public static bool TryParse(
        IEnumerable<string> arguments,
        out TerminalInputLabOptions options,
        out string? error)
    {
        options = new();
        error = null;
        string[] args = arguments.ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manual":
                    break;
                case "--mouse-all-motion":
                    options = options with { MouseAllMotion = true };
                    break;
                case "--no-enhanced-keyboard":
                    options = options with { EnhancedKeyboard = false };
                    break;
                case "--escape-timeout-ms":
                    if (!TryReadPositive(args, ref i, out int escapeTimeout))
                        return Fail("--escape-timeout-ms requires a positive integer.", out options, out error);
                    options = options with { EscapeTimeoutMilliseconds = escapeTimeout };
                    break;
                case "--step-seconds":
                    if (!TryReadPositive(args, ref i, out int stepSeconds))
                        return Fail("--step-seconds requires a positive integer.", out options, out error);
                    options = options with { StepSeconds = stepSeconds };
                    break;
                default:
                    return Fail($"Unknown input-lab option: {args[i]}", out options, out error);
            }
        }

        return true;
    }

    private static bool TryReadPositive(string[] args, ref int index, out int value)
    {
        value = 0;
        return ++index < args.Length &&
               int.TryParse(args[index], out value) &&
               value > 0;
    }

    private static bool Fail(
        string message,
        out TerminalInputLabOptions options,
        out string? error)
    {
        options = new();
        error = message;
        return false;
    }
}

public static class TerminalInputLab
{
    public static int RunLinux(string[] arguments) =>
        Run(arguments, TerminalInputLabPlatform.Linux);

    public static int RunMacOs(string[] arguments) =>
        Run(arguments, TerminalInputLabPlatform.MacOs);

    private static int Run(string[] arguments, TerminalInputLabPlatform platform)
    {
        if (!TerminalInputLabOptions.TryParse(arguments, out var options, out string? error))
        {
            SystemConsole.Error.WriteLine(error);
            return 2;
        }

        return Run(options, platform);
    }

    private const string Csi = "\x1b[";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static int Run(TerminalInputLabOptions options, TerminalInputLabPlatform platform)
    {
        if (SystemConsole.IsInputRedirected || SystemConsole.IsOutputRedirected)
        {
            SystemConsole.Error.WriteLine("Terminal Input Lab requires stdin/stdout attached to a terminal.");
            return 1;
        }

        using AnsiTerminalConsoleDriver driver = platform switch
        {
            TerminalInputLabPlatform.Linux => AnsiTerminalConsoleDriver.CreateLinux(options.EnhancedKeyboard),
            TerminalInputLabPlatform.MacOs => AnsiTerminalConsoleDriver.CreateMacOs(options.EnhancedKeyboard),
            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
        };

        string artifactDirectory = Path.GetFullPath(Path.Combine("artifacts", "terminal-input-lab"));
        Directory.CreateDirectory(artifactDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string environmentName = GetEnvironmentName();
        string jsonPath = Path.Combine(artifactDirectory, $"{stamp}-{environmentName}.jsonl");
        string summaryPath = Path.Combine(artifactDirectory, $"{stamp}-summary.md");
        using var log = new StreamWriter(jsonPath, append: false) { AutoFlush = true };
        var parser = new TerminalInputLabParser();
        var observations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        driver.EnableRawInputMode();
        KeyboardProtocolSnapshot keyboard = driver.KeyboardProtocol;
        try
        {
            SystemConsole.Write("\x1b]0;CSharpFar Terminal Input Lab\x07");
            driver.WriteRawControl(options.MouseAllMotion
                ? $"{Csi}?1003h{Csi}?1006h"
                : $"{Csi}?1002h{Csi}?1006h");

            PrintHeader(options, jsonPath, keyboard);
            RunManualSteps(driver, parser, log, options, observations);
        }
        finally
        {
            driver.WriteRawControl($"{Csi}?1000l{Csi}?1002l{Csi}?1003l{Csi}?1006l");
            driver.RestoreTerminal();
        }

        keyboard = driver.KeyboardProtocol;
        WriteSummary(summaryPath, jsonPath, keyboard, options, observations);
        PrintSummary(jsonPath, summaryPath, keyboard, options, observations);
        return 0;
    }

    private static void RunManualSteps(
        AnsiTerminalConsoleDriver driver,
        TerminalInputLabParser parser,
        StreamWriter log,
        TerminalInputLabOptions options,
        HashSet<string> observations)
    {
        List<LabStep> steps = CreateSteps(options.MouseAllMotion);
        DateTimeOffset? lastExitSignal = null;
        string? lastExitKey = null;
        for (int index = 0; index < steps.Count; index++)
        {
            LabStep step = steps[index];
            SystemConsole.WriteLine();
            SystemConsole.WriteLine($"Step {index + 1}/{steps.Count}: {step.Instruction}");
            SystemConsole.WriteLine($"You have {options.StepSeconds} seconds.");
            DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(options.StepSeconds);
            var stepObservations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lastRemaining = options.StepSeconds + 1;

            while (DateTimeOffset.Now < deadline)
            {
                int remaining = Math.Max(1, (int)Math.Ceiling((deadline - DateTimeOffset.Now).TotalSeconds));
                if (remaining != lastRemaining)
                {
                    SystemConsole.WriteLine($"  {remaining}...");
                    lastRemaining = remaining;
                }

                if (!driver.TryReadRawInput(100, options.EscapeTimeoutMilliseconds, out AnsiInputReadResult? raw))
                    continue;

                TerminalInputLabEvent parsed = parser.Parse(raw.Bytes);
                string signature = Signature(parsed);
                observations.Add(signature);
                stepObservations.Add(signature);
                PrintEvent(parsed);
                WriteJson(log, step.Name, parsed);

                if (IsExitSignal(parsed, out string? exitKey))
                {
                    DateTimeOffset now = DateTimeOffset.Now;
                    if (lastExitKey == exitKey &&
                        lastExitSignal.HasValue &&
                        now - lastExitSignal.Value < TimeSpan.FromSeconds(1))
                    {
                        return;
                    }

                    lastExitKey = exitKey;
                    lastExitSignal = now;
                }
            }

            int observed = step.Expected.Count(stepObservations.Contains);
            bool unknown = stepObservations.Any(value =>
                value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Malformed", StringComparison.OrdinalIgnoreCase));
            string result = observed == step.Expected.Length
                ? "observed"
                : observed == 0 && unknown
                    ? "unknown"
                    : observed == 0
                        ? "not observed"
                        : $"partial ({observed}/{step.Expected.Length})";
            SystemConsole.WriteLine($"Result: {result}");
        }
    }

    private static List<LabStep> CreateSteps(bool allMotion)
    {
        var steps = new List<LabStep>
        {
            new("Plain keys", "Press A, B, 1, Space.", ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"]),
            new(
                "Enter combinations",
                "Press Enter, Shift+Enter, Ctrl+Enter, Ctrl+Shift+Enter.",
                ["Key:Enter", "Key:Enter:Shift", "Key:Enter:Control", "Key:Enter:Control+Shift"]),
            new("Navigation arrows", "Press ArrowUp, ArrowDown, ArrowLeft, ArrowRight.", ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"]),
            new("Navigation keys", "Press Home, End, PageUp, PageDown, Insert, Delete.", ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"]),
            new("Tab / Backspace / Esc", "Press Tab, Backspace, Esc.", ["Key:Tab", "Key:Backspace", "Key:Escape"]),
            new("Shift+Tab", "Press Shift+Tab.", ["Key:Tab:Shift"]),
            new("F1-F4", "Press F1, F2, F3, F4.", ["Key:F1", "Key:F2", "Key:F3", "Key:F4"]),
            new("F5-F12", "Press F5, F6, F7, F8, F9, F10, F11, F12.", ["Key:F5", "Key:F6", "Key:F7", "Key:F8", "Key:F9", "Key:F10", "Key:F11", "Key:F12"]),
            new("Modified function keys", "Press Shift+F5, Ctrl+F5, Alt+F5. Do not press Alt+F4.", ["Key:F5:Shift", "Key:F5:Control", "Key:F5:Alt"]),
            new("Alt combinations", "Press Alt+A, Alt+Left, Alt+Right.", ["Key:A:Alt", "Key:LeftArrow:Alt", "Key:RightArrow:Alt"]),
            new("Ctrl combinations", "Press Ctrl+A, Ctrl+Left, Ctrl+Right.", ["Key:A:Control", "Key:LeftArrow:Control", "Key:RightArrow:Control"]),
            new("Mouse click", "Click left, right and middle mouse buttons inside this terminal.", ["Mouse:LeftDown", "Mouse:LeftUp", "Mouse:RightDown", "Mouse:RightUp", "Mouse:MiddleDown", "Mouse:MiddleUp"]),
            new("Mouse wheel", "Scroll mouse wheel up and down inside this terminal.", ["Mouse:WheelUp", "Mouse:WheelDown"]),
            new("Mouse drag", "Hold left mouse button and drag inside this terminal.", ["Mouse:LeftDown", "Mouse:MoveWithButton", "Mouse:LeftUp"]),
        };
        if (allMotion)
            steps.Add(new("Mouse all-motion", "Move mouse inside terminal without pressing buttons.", ["Mouse:MoveNoButton"]));
        return steps;
    }

    private static void PrintHeader(
        TerminalInputLabOptions options,
        string jsonPath,
        KeyboardProtocolSnapshot keyboard)
    {
        SystemConsole.WriteLine("CSharpFar Terminal Input Lab");
        SystemConsole.WriteLine("Environment:");
        SystemConsole.WriteLine($"  OS: {RuntimeInformation.OSDescription}");
        SystemConsole.WriteLine($"  TERM: {Environment.GetEnvironmentVariable("TERM") ?? "<unset>"}");
        SystemConsole.WriteLine($"  WT_SESSION: {Environment.GetEnvironmentVariable("WT_SESSION") ?? "<unset>"}");
        SystemConsole.WriteLine($"  COLORTERM: {Environment.GetEnvironmentVariable("COLORTERM") ?? "<unset>"}");
        SystemConsole.WriteLine($"  TTY: {Environment.GetEnvironmentVariable("TTY") ?? Environment.GetEnvironmentVariable("SSH_TTY") ?? "<unknown>"}");
        SystemConsole.WriteLine("Modes:");
        SystemConsole.WriteLine("  Raw input: enabled");
        SystemConsole.WriteLine($"  Mouse: {(options.MouseAllMotion ? "1003 + 1006" : "1002 + 1006")}");
        PrintKeyboardState(keyboard);
        SystemConsole.WriteLine($"  Escape timeout: {options.EscapeTimeoutMilliseconds} ms");
        SystemConsole.WriteLine($"Log: {Path.GetRelativePath(Directory.GetCurrentDirectory(), jsonPath)}");
        SystemConsole.WriteLine("Press Ctrl+C twice or Esc twice to exit.");
    }

    private static void PrintKeyboardState(KeyboardProtocolSnapshot keyboard)
    {
        SystemConsole.WriteLine($"  Keyboard protocol: {keyboard.Protocol}");
        SystemConsole.WriteLine($"  Protocol support: {keyboard.SupportStatus}");
        SystemConsole.WriteLine($"  Requested keyboard flags: {keyboard.RequestedFlags}");
        SystemConsole.WriteLine($"  Confirmed keyboard flags: {keyboard.ConfirmedFlags?.ToString() ?? "unavailable"}");
        SystemConsole.WriteLine($"  Protocol active: {keyboard.IsActive}");
    }

    private static void PrintEvent(TerminalInputLabEvent input)
    {
        SystemConsole.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Raw: {FormatBytes(input.RawBytes)}");
        SystemConsole.WriteLine($"               Raw text: {FormatText(input.RawBytes)}");
        if (input.Kind == "Mouse")
            SystemConsole.WriteLine($"               Parsed: Mouse {input.MouseEvent} terminal=({input.TerminalX},{input.TerminalY}) ui=({input.UiX},{input.UiY})");
        else if (input.Key.HasValue)
            SystemConsole.WriteLine($"               Parsed key: {input.Key.Value.Key}");
        else
            SystemConsole.WriteLine($"               Parsed: {input.Kind}{(input.Error is null ? "" : $" ({input.Error})")}");

        SystemConsole.WriteLine($"               Modifiers: {(input.Key.HasValue ? FormatModifiers(input.Key.Value.Modifiers) : "None")}");
        SystemConsole.WriteLine($"               Associated text: {FormatAssociatedText(input.AssociatedText)}");
        SystemConsole.WriteLine($"               Parser kind: {input.Kind}");
        SystemConsole.WriteLine($"               Protocol: {input.Protocol ?? "unknown"}");
    }

    private static void WriteJson(StreamWriter log, string step, TerminalInputLabEvent input)
    {
        var value = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.Now,
            ["step"] = step,
            ["rawHex"] = FormatBytes(input.RawBytes),
            ["rawText"] = FormatText(input.RawBytes),
            ["kind"] = input.Kind,
            ["key"] = input.Key?.Key.ToString(),
            ["modifiers"] = input.Key.HasValue ? GetModifiers(input.Key.Value.Modifiers) : null,
            ["associatedText"] = input.AssociatedText,
            ["protocol"] = input.Protocol,
            ["keyEventType"] = input.KeyEventType?.ToString(),
            ["modifierKey"] = input.ModifierKeyName,
            ["mouseEvent"] = input.MouseEvent,
            ["mouseButton"] = input.MouseButton?.ToString(),
            ["buttonCode"] = input.ButtonCode,
            ["terminalX"] = input.TerminalX,
            ["terminalY"] = input.TerminalY,
            ["uiX"] = input.UiX,
            ["uiY"] = input.UiY,
            ["isKnown"] = input.IsKnown,
            ["error"] = input.Error,
        };
        log.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void PrintSummary(
        string jsonPath,
        string summaryPath,
        KeyboardProtocolSnapshot keyboard,
        TerminalInputLabOptions options,
        HashSet<string> observed)
    {
        SystemConsole.WriteLine();
        SystemConsole.WriteLine("Terminal Input Lab Summary");
        PrintKeyboardState(keyboard);
        SystemConsole.WriteLine($"Enter: {Status(observed, ["Key:Enter"])}");
        SystemConsole.WriteLine($"Shift+Enter: {Status(observed, ["Key:Enter:Shift"])}");
        SystemConsole.WriteLine($"Ctrl+Enter: {EnhancedStatus(observed, "Key:Enter:Control", keyboard)}");
        SystemConsole.WriteLine($"Ctrl+Shift+Enter: {EnhancedStatus(observed, "Key:Enter:Control+Shift", keyboard)}");
        SystemConsole.WriteLine($"Plain keys: {Status(observed, ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"])}");
        SystemConsole.WriteLine($"Arrows: {Status(observed, ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"])}");
        SystemConsole.WriteLine($"Navigation keys: {Status(observed, ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"])}");
        SystemConsole.WriteLine($"Shift+Tab: {Status(observed, ["Key:Tab:Shift"])}");
        SystemConsole.WriteLine($"F1-F12: {Status(observed, Enumerable.Range(1, 12).Select(i => $"Key:F{i}"))}");
        SystemConsole.WriteLine($"Mouse click: {Status(observed, ["Mouse:LeftDown", "Mouse:LeftUp"])}");
        SystemConsole.WriteLine($"Mouse wheel: {Status(observed, ["Mouse:WheelUp", "Mouse:WheelDown"])}");
        SystemConsole.WriteLine($"Mouse drag: {Status(observed, ["Mouse:MoveWithButton"])}");
        SystemConsole.WriteLine($"Motion without buttons: {(options.MouseAllMotion ? Status(observed, ["Mouse:MoveNoButton"]) : "skipped")}");
        SystemConsole.WriteLine($"Log: {jsonPath}");
        SystemConsole.WriteLine($"Summary: {summaryPath}");
        SystemConsole.WriteLine("Manual physical keyboard/mouse test is still required.");
    }

    private static void WriteSummary(
        string path,
        string jsonPath,
        KeyboardProtocolSnapshot keyboard,
        TerminalInputLabOptions options,
        HashSet<string> observed)
    {
        string[] lines =
        [
            "# Terminal Input Lab Summary", "",
            $"- Timestamp: {DateTimeOffset.Now:O}",
            $"- OS: {RuntimeInformation.OSDescription}",
            $"- TERM: {Environment.GetEnvironmentVariable("TERM") ?? "<unset>"}",
            $"- Keyboard protocol: {keyboard.Protocol}",
            $"- Protocol support: {keyboard.SupportStatus}",
            $"- Requested keyboard flags: {keyboard.RequestedFlags}",
            $"- Confirmed keyboard flags: {keyboard.ConfirmedFlags?.ToString() ?? "unavailable"}",
            $"- Protocol active: {keyboard.IsActive}", "",
            "| Check | Result |", "|---|---|",
            $"| Enter | {Status(observed, ["Key:Enter"])} |",
            $"| Shift+Enter | {Status(observed, ["Key:Enter:Shift"])} |",
            $"| Ctrl+Enter | {EnhancedStatus(observed, "Key:Enter:Control", keyboard)} |",
            $"| Ctrl+Shift+Enter | {EnhancedStatus(observed, "Key:Enter:Control+Shift", keyboard)} |",
            $"| Plain keys | {Status(observed, ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"])} |",
            $"| Arrows | {Status(observed, ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"])} |",
            $"| Navigation keys | {Status(observed, ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"])} |",
            $"| Shift+Tab | {Status(observed, ["Key:Tab:Shift"])} |",
            $"| F1-F12 | {Status(observed, Enumerable.Range(1, 12).Select(i => $"Key:F{i}"))} |",
            $"| Mouse click | {Status(observed, ["Mouse:LeftDown", "Mouse:LeftUp"])} |",
            $"| Mouse wheel | {Status(observed, ["Mouse:WheelUp", "Mouse:WheelDown"])} |",
            $"| Mouse drag | {Status(observed, ["Mouse:MoveWithButton"])} |",
            $"| Motion without buttons | {(options.MouseAllMotion ? Status(observed, ["Mouse:MoveNoButton"]) : "skipped")} |", "",
            $"JSONL log: `{jsonPath}`", "",
            "Manual physical keyboard/mouse test is still required.",
        ];
        File.WriteAllLines(path, lines);
    }

    private static string Signature(TerminalInputLabEvent input)
    {
        if (input.Kind == "Mouse")
            return $"Mouse:{input.MouseEvent}";
        if (input.Kind == "ModifierKey")
        {
            string name = input.ModifierKeyName?
                .Replace("LEFT_", "", StringComparison.Ordinal)
                .Replace("RIGHT_", "", StringComparison.Ordinal) ?? "UNKNOWN";
            return $"Modifier:{name}:{input.KeyEventType}";
        }

        if (!input.Key.HasValue)
            return input.Kind;

        string modifiers = FormatModifiers(input.Key.Value.Modifiers);
        return $"Key:{input.Key.Value.Key}{(modifiers == "None" ? "" : $":{modifiers}")}";
    }

    private static bool IsExitSignal(TerminalInputLabEvent input, out string? key)
    {
        key = null;
        if (!input.Key.HasValue)
            return false;
        if (input.Key.Value.Key == ConsoleKey.Escape)
            key = "Escape";
        else if (input.Key.Value.Key == ConsoleKey.C &&
                 input.Key.Value.Modifiers.HasFlag(ConsoleModifiers.Control))
            key = "Ctrl+C";

        return key is not null;
    }

    private static string EnhancedStatus(
        HashSet<string> observed,
        string expected,
        KeyboardProtocolSnapshot keyboard)
    {
        if (observed.Contains(expected))
            return "OK";
        return keyboard.IsActive ? "not observed" : "not observable by terminal";
    }

    private static string Status(HashSet<string> observed, IEnumerable<string> expected)
    {
        string[] values = expected.ToArray();
        int count = values.Count(observed.Contains);
        return count == values.Length
            ? "OK"
            : count == 0
                ? "not observed"
                : $"partial ({count}/{values.Length})";
    }

    private static string[] GetModifiers(ConsoleModifiers modifiers)
    {
        var values = new List<string>(3);
        if (modifiers.HasFlag(ConsoleModifiers.Control))
            values.Add(nameof(ConsoleModifiers.Control));
        if (modifiers.HasFlag(ConsoleModifiers.Shift))
            values.Add(nameof(ConsoleModifiers.Shift));
        if (modifiers.HasFlag(ConsoleModifiers.Alt))
            values.Add(nameof(ConsoleModifiers.Alt));
        return values.ToArray();
    }

    private static string FormatModifiers(ConsoleModifiers modifiers) =>
        modifiers == 0 ? "None" : string.Join('+', GetModifiers(modifiers));

    private static string FormatAssociatedText(string? text) =>
        string.IsNullOrEmpty(text) ? "<none>" : JsonSerializer.Serialize(text);

    private static string FormatBytes(IEnumerable<byte> bytes) =>
        string.Join(' ', bytes.Select(x => x.ToString("X2")));

    private static string FormatText(IEnumerable<byte> bytes) =>
        string.Join(' ', bytes.Select(static b => b switch
        {
            0x1b => "ESC",
            0x09 => "TAB",
            0x0d => "CR",
            0x0a => "LF",
            0x7f => "DEL",
            >= 0x20 and <= 0x7e => ((char)b).ToString(),
            _ => $"0x{b:X2}",
        }));

    private static string GetEnvironmentName() =>
        Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null &&
        Environment.GetEnvironmentVariable("WT_SESSION") is not null
            ? "wsl-windows-terminal"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "macos"
                : "unix";

    private sealed record LabStep(string Name, string Instruction, string[] Expected);
}
