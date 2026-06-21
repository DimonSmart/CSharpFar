using CSharpFar.Console.Ansi;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed record TerminalInputLabOptions(
    bool Manual = true,
    bool MouseAllMotion = false,
    bool EnhancedKeyboard = true,
    int EscapeTimeoutMilliseconds = 50,
    int StepSeconds = 5)
{
    public static bool TryParse(IEnumerable<string> arguments, out TerminalInputLabOptions options, out string? error)
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
        return ++index < args.Length && int.TryParse(args[index], out value) && value > 0;
    }

    private static bool Fail(string message, out TerminalInputLabOptions options, out string? error)
    {
        options = new();
        error = message;
        return false;
    }
}

internal static class TerminalInputLab
{
    private const string Csi = "\x1b[";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static int Run(TerminalInputLabOptions options)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Terminal Input Lab requires stdin/stdout attached to a terminal.");
            return 1;
        }

        using var driver = new AnsiTerminalConsoleDriver();
        string artifactDirectory = Path.GetFullPath(Path.Combine("artifacts", "terminal-input-lab"));
        Directory.CreateDirectory(artifactDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string environmentName = GetEnvironmentName();
        string jsonPath = Path.Combine(artifactDirectory, $"{stamp}-{environmentName}.jsonl");
        string summaryPath = Path.Combine(artifactDirectory, $"{stamp}-summary.md");
        using var log = new StreamWriter(jsonPath, append: false) { AutoFlush = true };
        var parser = new TerminalInputLabParser();
        var observations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string enhancedResponse = options.EnhancedKeyboard ? "timeout" : "disabled";
        bool enhancedPushed = false;

        driver.EnableRawInputMode();
        try
        {
            Console.Write("\x1b]0;CSharpFar Terminal Input Lab\x07");
            driver.WriteRawControl(options.MouseAllMotion
                ? $"{Csi}?1003h{Csi}?1006h"
                : $"{Csi}?1002h{Csi}?1006h");

            if (options.EnhancedKeyboard)
            {
                int? before = QueryEnhancedFlags(driver, options.EscapeTimeoutMilliseconds);
                driver.WriteRawControl($"{Csi}>u{Csi}=11;1u");
                enhancedPushed = true;
                int? after = QueryEnhancedFlags(driver, options.EscapeTimeoutMilliseconds);
                int? flags = after ?? before;
                enhancedResponse = flags.HasValue ? ((flags.Value & 11) == 11 ? "supported" : "unsupported") : "timeout";
            }

            PrintHeader(options, jsonPath, enhancedResponse);
            RunManualSteps(driver, parser, log, options, observations);
        }
        finally
        {
            driver.WriteRawControl($"{Csi}?1000l{Csi}?1002l{Csi}?1003l{Csi}?1006l");
            if (enhancedPushed)
                driver.WriteRawControl($"{Csi}<u");
            driver.RestoreTerminal();
        }

        WriteSummary(summaryPath, jsonPath, enhancedResponse, options, observations);
        PrintSummary(jsonPath, summaryPath, enhancedResponse, options, observations);
        return 0;
    }

    private static void RunManualSteps(
        AnsiTerminalConsoleDriver driver,
        TerminalInputLabParser parser,
        StreamWriter log,
        TerminalInputLabOptions options,
        HashSet<string> observations)
    {
        var steps = CreateSteps(options.MouseAllMotion);
        DateTimeOffset? lastExitSignal = null;
        string? lastExitKey = null;
        for (int index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            Console.WriteLine();
            Console.WriteLine($"Step {index + 1}/{steps.Count}: {step.Instruction}");
            Console.WriteLine($"You have {options.StepSeconds} seconds.");
            DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(options.StepSeconds);
            var stepObservations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lastRemaining = options.StepSeconds + 1;

            while (DateTimeOffset.Now < deadline)
            {
                int remaining = Math.Max(1, (int)Math.Ceiling((deadline - DateTimeOffset.Now).TotalSeconds));
                if (remaining != lastRemaining)
                {
                    Console.WriteLine($"  {remaining}...");
                    lastRemaining = remaining;
                }

                if (!driver.TryReadRawInput(100, options.EscapeTimeoutMilliseconds, out var input))
                    continue;

                var parsed = parser.Parse(input.Bytes);
                string signature = Signature(parsed);
                observations.Add(signature);
                stepObservations.Add(signature);
                PrintEvent(parsed);
                WriteJson(log, step.Name, parsed);

                if (IsExitSignal(parsed, out string? exitKey))
                {
                    var now = DateTimeOffset.Now;
                    if (lastExitKey == exitKey && lastExitSignal.HasValue && now - lastExitSignal.Value < TimeSpan.FromSeconds(1))
                        return;
                    lastExitKey = exitKey;
                    lastExitSignal = now;
                }
            }

            int observed = step.Expected.Count(stepObservations.Contains);
            bool unknown = stepObservations.Any(value => value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) || value == "MalformedMouse");
            string result = observed == step.Expected.Length ? "observed" : observed == 0 && unknown ? "unknown" : observed == 0 ? "not observed" : $"partial ({observed}/{step.Expected.Length})";
            Console.WriteLine($"Result: {result}");
        }
    }

    private static List<LabStep> CreateSteps(bool allMotion)
    {
        var steps = new List<LabStep>
        {
            new("Plain keys", "Press A, B, 1, Space.", ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"]),
            new("Navigation arrows", "Press ArrowUp, ArrowDown, ArrowLeft, ArrowRight.", ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"]),
            new("Navigation keys", "Press Home, End, PageUp, PageDown, Insert, Delete.", ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"]),
            new("Enter / Tab / Backspace / Esc", "Press Enter, Tab, Backspace, Esc.", ["Key:Enter", "Key:Tab", "Key:Backspace", "Key:Escape"]),
            new("Shift+Tab", "Press Shift+Tab.", ["Key:Tab:Shift"]),
            new("F1-F4", "Press F1, F2, F3, F4.", ["Key:F1", "Key:F2", "Key:F3", "Key:F4"]),
            new("F5-F12", "Press F5, F6, F7, F8, F9, F10, F11, F12. F11 may toggle Windows Terminal fullscreen instead of reaching the app.", ["Key:F5", "Key:F6", "Key:F7", "Key:F8", "Key:F9", "Key:F10", "Key:F11", "Key:F12"]),
            new("Modified function keys", "Press Shift+F5, Ctrl+F5, Alt+F5. Do not press Alt+F4.", ["Key:F5:Shift", "Key:F5:Control", "Key:F5:Alt"]),
            new("Alt combinations", "Press Alt+A, Alt+Left, Alt+Right.", ["Key:A:Alt", "Key:LeftArrow:Alt", "Key:RightArrow:Alt"]),
            new("Ctrl combinations", "Press Ctrl+A, Ctrl+Left, Ctrl+Right.", ["Key:A:Control", "Key:LeftArrow:Control", "Key:RightArrow:Control"]),
            new("Modifier-only keys", "Press and release Alt alone, Ctrl alone, Shift alone. This may produce no events.", ["Modifier:ALT:Press", "Modifier:ALT:Release", "Modifier:CONTROL:Press", "Modifier:CONTROL:Release", "Modifier:SHIFT:Press", "Modifier:SHIFT:Release"]),
            new("Mouse click", "Click left, right and middle mouse buttons inside this terminal.", ["Mouse:LeftDown", "Mouse:LeftUp", "Mouse:RightDown", "Mouse:RightUp", "Mouse:MiddleDown", "Mouse:MiddleUp"]),
            new("Mouse wheel", "Scroll mouse wheel up and down inside this terminal.", ["Mouse:WheelUp", "Mouse:WheelDown"]),
            new("Mouse drag", "Hold left mouse button and drag inside this terminal.", ["Mouse:LeftDown", "Mouse:MoveWithButton", "Mouse:LeftUp"]),
        };
        if (allMotion)
            steps.Add(new("Mouse all-motion", "Move mouse inside terminal without pressing buttons.", ["Mouse:MoveNoButton"]));
        return steps;
    }

    private static void PrintHeader(TerminalInputLabOptions options, string jsonPath, string enhancedResponse)
    {
        Console.WriteLine("CSharpFar Terminal Input Lab");
        Console.WriteLine("Environment:");
        Console.WriteLine($"  OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"  TERM: {Environment.GetEnvironmentVariable("TERM") ?? "<unset>"}");
        Console.WriteLine($"  WT_SESSION: {Environment.GetEnvironmentVariable("WT_SESSION") ?? "<unset>"}");
        Console.WriteLine($"  COLORTERM: {Environment.GetEnvironmentVariable("COLORTERM") ?? "<unset>"}");
        Console.WriteLine($"  TTY: {Environment.GetEnvironmentVariable("TTY") ?? Environment.GetEnvironmentVariable("SSH_TTY") ?? "<unknown>"}");
        Console.WriteLine("Modes:");
        Console.WriteLine("  Raw input: enabled");
        Console.WriteLine($"  Mouse: {(options.MouseAllMotion ? "1003 + 1006" : "1002 + 1006")}");
        Console.WriteLine($"  Enhanced keyboard: {enhancedResponse}");
        Console.WriteLine($"  Escape timeout: {options.EscapeTimeoutMilliseconds} ms");
        Console.WriteLine($"Log: {Path.GetRelativePath(Directory.GetCurrentDirectory(), jsonPath)}");
        Console.WriteLine("Press Ctrl+C twice or Esc twice to exit.");
    }

    private static void PrintEvent(TerminalInputLabEvent input)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Raw: {FormatBytes(input.RawBytes)}");
        Console.WriteLine($"               Text: {FormatText(input.RawBytes)}");
        if (input.Kind == "Mouse")
            Console.WriteLine($"               Parsed: Mouse {input.MouseEvent} terminal=({input.TerminalX},{input.TerminalY}) ui=({input.UiX},{input.UiY})");
        else if (input.Key.HasValue)
            Console.WriteLine($"               Parsed: Key={input.Key.Value.Key} Modifiers={FormatModifiers(input.Key.Value.Modifiers)}");
        else
            Console.WriteLine($"               Parsed: {input.Kind}{(input.Error is null ? "" : $" ({input.Error})")}");
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

    private static void PrintSummary(string jsonPath, string summaryPath, string enhanced, TerminalInputLabOptions options, HashSet<string> observed)
    {
        Console.WriteLine();
        Console.WriteLine("Terminal Input Lab Summary");
        Console.WriteLine($"Enhanced keyboard response: {enhanced}");
        Console.WriteLine($"Plain keys: {Status(observed, ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"])}");
        Console.WriteLine($"Arrows: {Status(observed, ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"])}");
        Console.WriteLine($"Navigation keys: {Status(observed, ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"])}");
        Console.WriteLine($"Shift+Tab: {Status(observed, ["Key:Tab:Shift"])}");
        Console.WriteLine($"F1-F12: {Status(observed, Enumerable.Range(1, 12).Select(i => $"Key:F{i}"))}");
        Console.WriteLine($"Shift+F5: {Status(observed, ["Key:F5:Shift"])}");
        Console.WriteLine($"Alt+A: {Status(observed, ["Key:A:Alt"])}");
        Console.WriteLine($"Alt+Left: {Status(observed, ["Key:LeftArrow:Alt"])}");
        Console.WriteLine($"Ctrl+Right: {Status(observed, ["Key:RightArrow:Control"])}");
        Console.WriteLine($"Standalone Esc: {Status(observed, ["Key:Escape"])}");
        Console.WriteLine($"Modifier-only Alt/Ctrl/Shift: {Status(observed, ModifierOnlyExpected("ALT", "CONTROL", "SHIFT"))}");
        Console.WriteLine($"Mouse click: {Status(observed, ["Mouse:LeftDown", "Mouse:LeftUp"])}");
        Console.WriteLine($"Mouse wheel: {Status(observed, ["Mouse:WheelUp", "Mouse:WheelDown"])}");
        Console.WriteLine($"Mouse drag: {Status(observed, ["Mouse:MoveWithButton"])}");
        Console.WriteLine($"Motion without buttons: {(options.MouseAllMotion ? Status(observed, ["Mouse:MoveNoButton"]) : "skipped")}");
        Console.WriteLine($"Log: {jsonPath}");
        Console.WriteLine($"Summary: {summaryPath}");
        Console.WriteLine("Manual physical keyboard/mouse test is still required.");
    }

    private static void WriteSummary(string path, string jsonPath, string enhanced, TerminalInputLabOptions options, HashSet<string> observed)
    {
        string[] lines =
        [
            "# Terminal Input Lab Summary", "",
            $"- Timestamp: {DateTimeOffset.Now:O}",
            $"- OS: {RuntimeInformation.OSDescription}",
            $"- TERM: {Environment.GetEnvironmentVariable("TERM") ?? "<unset>"}",
            $"- Windows Terminal: {(Environment.GetEnvironmentVariable("WT_SESSION") is null ? "not detected" : "detected")}",
            $"- WSL: {(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is null ? "not detected" : "detected")}",
            $"- Enhanced keyboard response: {enhanced}", "",
            "| Check | Result |", "|---|---|",
            $"| Plain keys | {Status(observed, ["Key:A", "Key:B", "Key:D1", "Key:Spacebar"])} |",
            $"| Arrows | {Status(observed, ["Key:UpArrow", "Key:DownArrow", "Key:LeftArrow", "Key:RightArrow"])} |",
            $"| Navigation keys | {Status(observed, ["Key:Home", "Key:End", "Key:PageUp", "Key:PageDown", "Key:Insert", "Key:Delete"])} |",
            $"| Enter / Tab / Backspace / Esc | {Status(observed, ["Key:Enter", "Key:Tab", "Key:Backspace", "Key:Escape"])} |",
            $"| Shift+Tab | {Status(observed, ["Key:Tab:Shift"])} |",
            $"| F1-F12 | {Status(observed, Enumerable.Range(1, 12).Select(i => $"Key:F{i}"))} |",
            $"| Shift+F5 | {Status(observed, ["Key:F5:Shift"])} |",
            $"| Ctrl+F5 | {Status(observed, ["Key:F5:Control"])} |",
            $"| Alt+F5 | {Status(observed, ["Key:F5:Alt"])} |",
            $"| Alt+A | {Status(observed, ["Key:A:Alt"])} |",
            $"| Alt+arrows | {Status(observed, ["Key:LeftArrow:Alt", "Key:RightArrow:Alt"])} |",
            $"| Alt alone | {Status(observed, ModifierOnlyExpected("ALT"))} |",
            $"| Ctrl alone | {Status(observed, ModifierOnlyExpected("CONTROL"))} |",
            $"| Shift alone | {Status(observed, ModifierOnlyExpected("SHIFT"))} |",
            $"| Mouse click | {Status(observed, ["Mouse:LeftDown", "Mouse:LeftUp"])} |",
            $"| Right click | {Status(observed, ["Mouse:RightDown", "Mouse:RightUp"])} |",
            $"| Middle click | {Status(observed, ["Mouse:MiddleDown", "Mouse:MiddleUp"])} |",
            $"| Mouse wheel | {Status(observed, ["Mouse:WheelUp", "Mouse:WheelDown"])} |",
            $"| Mouse drag | {Status(observed, ["Mouse:MoveWithButton"])} |",
            $"| Motion without buttons | {(options.MouseAllMotion ? Status(observed, ["Mouse:MoveNoButton"]) : "skipped")} |", "",
            $"JSONL log: `{jsonPath}`", "",
            "Manual physical keyboard/mouse test is still required.",
        ];
        File.WriteAllLines(path, lines);
    }

    private static int? QueryEnhancedFlags(AnsiTerminalConsoleDriver driver, int escapeTimeout)
    {
        driver.WriteRawControl($"{Csi}?u");
        if (!driver.TryReadRawInput(200, escapeTimeout, out var response))
            return null;
        string text = System.Text.Encoding.ASCII.GetString(response.Bytes);
        return text.StartsWith("\x1b[?", StringComparison.Ordinal) && text.EndsWith('u') &&
            int.TryParse(text[3..^1], out int flags) ? flags : null;
    }

    private static string Signature(TerminalInputLabEvent input)
    {
        if (input.Kind == "Mouse")
            return $"Mouse:{input.MouseEvent}";
        if (input.Kind == "ModifierKey")
        {
            string name = input.ModifierKeyName?.Replace("LEFT_", "", StringComparison.Ordinal).Replace("RIGHT_", "", StringComparison.Ordinal) ?? "UNKNOWN";
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
        else if (input.Key.Value.Key == ConsoleKey.C && input.Key.Value.Modifiers.HasFlag(ConsoleModifiers.Control))
            key = "Ctrl+C";
        return key is not null;
    }

    private static string Status(HashSet<string> observed, IEnumerable<string> expected)
    {
        string[] values = expected.ToArray();
        int count = values.Count(observed.Contains);
        return count == values.Length ? "OK" : count == 0 ? "not observed" : $"partial ({count}/{values.Length})";
    }

    private static IEnumerable<string> ModifierOnlyExpected(params string[] names) =>
        names.SelectMany(name => new[] { $"Modifier:{name}:Press", $"Modifier:{name}:Release" });

    private static string[] GetModifiers(ConsoleModifiers modifiers) =>
        Enum.GetValues<ConsoleModifiers>()
            .Where(value => value != 0 && modifiers.HasFlag(value))
            .Select(x => x.ToString())
            .ToArray();

    private static string FormatModifiers(ConsoleModifiers modifiers) =>
        modifiers == 0 ? "None" : string.Join('+', GetModifiers(modifiers));

    private static string FormatBytes(IEnumerable<byte> bytes) => string.Join(' ', bytes.Select(x => x.ToString("X2")));

    private static string FormatText(IEnumerable<byte> bytes) => string.Join(' ', bytes.Select(static b => b switch
    {
        0x1b => "ESC", 0x09 => "TAB", 0x0d => "CR", 0x0a => "LF", 0x7f => "DEL",
        >= 0x20 and <= 0x7e => ((char)b).ToString(), _ => $"0x{b:X2}",
    }));

    private static string GetEnvironmentName() =>
        Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null && Environment.GetEnvironmentVariable("WT_SESSION") is not null
            ? "wsl-windows-terminal"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "unix";

    private sealed record LabStep(string Name, string Instruction, string[] Expected);
}
