using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (!OperatingSystem.IsWindows())
    return 2;

string root = Directory.GetCurrentDirectory();
string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
string artifacts = Path.Combine(root, "artifacts", "windows-scrollback-fix", stamp);
Directory.CreateDirectory(artifacts);
string app = Path.Combine(root, "src", "CSharpFar.Host.Windows", "bin", "Debug", "net10.0", "CSharpFar.dll");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
if (!File.Exists(app))
    throw new FileNotFoundException("Build CSharpFar.Host.Windows before running the experiment.", app);

var variants = new[]
{
    new PromptVariant("production", new(), Cycles: 1),
};

var results = variants.Select(variant => RunPromptVariantSafely(variant, Path.Combine(artifacts, variant.Name))).ToList();
File.WriteAllText(Path.Combine(artifacts, "summary.json"), JsonSerializer.Serialize(results.Single(), jsonOptions));
File.WriteAllText(Path.Combine(artifacts, "REPORT.md"), PromptReport(results, artifacts));
return results.All(x => x.Valid) ? 0 : 1;

PromptVariantResult RunPromptVariantSafely(PromptVariant variant, string path)
{
    try
    {
        return RunPromptVariant(variant, path);
    }
    catch (Exception ex)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "runner-error.txt"), ex.ToString());
        return PromptVariantResult.Invalid(variant.Name, ex.Message);
    }
}

PromptVariantResult RunPromptVariant(PromptVariant variant, string path)
{
    Directory.CreateDirectory(path);
    var variables = new Dictionary<string, string>(variant.Variables);
    using var session = ConsoleSession.Start(variant.Name, $"dotnet \"{app}\"", path, variables);
    session.WaitForConsole();
    session.Wait(3000);
    session.Resize(120, 30);
    session.Wait(500);
    session.CtrlO();
    session.Wait(500);
    session.SendText("for /L %i in (1,1,120) do @echo RUN_01_LINE_00%i");
    session.Enter();
    Require(session.WaitForText("RUN_01_LINE_00120", 12000), "CSharpFar did not execute the shell-output command.");
    session.Wait(400);

    string prompt = Directory.GetCurrentDirectory() + ">";
    var captures = new List<PromptCapture> { session.CapturePrompt("before-resize", prompt) };
    for (int cycle = 1; cycle <= variant.Cycles; cycle++)
    {
        foreach (int width in new[] { 108, 104, 108, 120 })
        {
            session.Resize(width, 30);
            session.Wait(25);
        }
        session.Wait(700);
        captures.Add(session.CapturePrompt($"cycle-{cycle}-width-burst-stable", prompt));
        session.Resize(100, 24);
        session.Wait(700);
        captures.Add(session.CapturePrompt($"cycle-{cycle}-combined-stable", prompt));
        session.Resize(120, 30);
        session.Wait(700);
        captures.Add(session.CapturePrompt($"cycle-{cycle}-restored-stable", prompt));
    }

    ScrollResult scroll = RunScrollCheck(session, prompt);
    for (int cycle = 1; cycle <= 5; cycle++)
    {
        session.CtrlO();
        session.Wait(250);
        session.Capture($"ctrl-o-{cycle}-panels");
        session.CtrlO();
        session.Wait(250);
        captures.Add(session.CapturePrompt($"ctrl-o-{cycle}-hidden", prompt));
    }

    string trace = session.ReadTrace();
    int capturedFullWrites = Regex.Matches(trace, @"WRITE type=Win32FrameBatch ownership=CapturedExternalSurface").Count;
    VariantResult history = session.Result("production", "resize-and-scroll", 1);
    bool duplicate = captures.Any(capture => capture.Prompts.Count(location => location.Visible) != 1 ||
        capture.Fragments.Any(location => location.Visible));
    bool valid = capturedFullWrites == 0 && scroll.Valid && scroll.ReturnedToBottomOnInput &&
        scroll.OnePromptAfterInput && !duplicate && !history.Corruption;
    var result = new PromptVariantResult(variant.Name, valid, duplicate, captures.Last().Prompts.Count, captures.Last().Prompts, captures.Last().Fragments, capturedFullWrites, scroll, captures, history, null);
    File.WriteAllText(Path.Combine(path, "variant-summary.json"), JsonSerializer.Serialize(result, jsonOptions));
    return result;
}

ScrollResult RunScrollCheck(ConsoleSession session, string prompt)
{
    if (!session.TryScrollViewportUp())
        return ScrollResult.Invalid;
    bool raised = !session.ViewportAtBottom;
    session.CapturePrompt("scroll-up", prompt);
    session.SendText("d");
    session.Wait(300);
    var after = session.CapturePrompt("scroll-after-input", prompt);
    return new ScrollResult(
        raised,
        session.ViewportAtBottom,
        after.Prompts.Count(location => location.Visible) == 1 &&
        !after.Fragments.Any(location => location.Visible) &&
        session.WaitForText(prompt + "d", 1000));
}

static string PromptReport(IReadOnlyList<PromptVariantResult> results, string artifacts)
{
    PromptVariantResult result = results.Single();
    return $"""
# Windows scrollback fix validation

## Implementation

Production behavior distinguishes application-owned frames from frames captured from the externally owned main console surface. Captured frames use dirty writes only. Hidden resize samples real geometry until a quiet interval, coalesces queued resize events, and then performs one recovery render.

## Real conhost result

| Проверка | Результат |
| --- | --- |
| numbered history after width resize | {(result.History.Corruption ? "FAIL" : "PASS")} |
| fragments | {result.History.FragmentCount} |
| missing markers | {result.History.MissingMarkers} |
| shifted markers | {result.History.MisalignedMarkerCount} |
| duplicate markers | {result.History.DuplicateMarkers} |
| captured full viewport writes | {result.CapturedFullViewportWrites} |
| duplicate visible prompt | {(result.DuplicatePrompt ? 1 : 0)} |
| scroll up | {(result.Scroll.Valid ? "PASS" : "FAIL")} |
| first input returns bottom | {(result.Scroll.ReturnedToBottomOnInput ? "PASS" : "FAIL")} |
| first character retained / one prompt | {(result.Scroll.OnePromptAfterInput ? "PASS" : "FAIL")} |
| Ctrl+O cycles | {(result.Captures.Count(capture => capture.Phase.StartsWith("ctrl-o-", StringComparison.Ordinal)) == 5 ? "PASS" : "FAIL")} |

## Remaining limitations

Windows Terminal is not required for acceptance and is not exercised by this classic-conhost harness.

Artifacts: `{artifacts}`.
""";
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ConsoleSession : IDisposable
{
    private readonly Process _cmd;
    private readonly string _path;
    private readonly List<CaptureState> _states = [];
    private readonly string _trace;
    private IntPtr _output;
    private IntPtr _input;
    private IntPtr _hwnd;

    private ConsoleSession(Process cmd, string path)
    {
        _cmd = cmd;
        _path = path;
        _trace = Path.Combine(path, "trace.log");
    }
    public static ConsoleSession Start(string name, string command, string path, Dictionary<string, string> vars)
    {
        string title = "CSharpFar experiment " + Guid.NewGuid().ToString("N");
        string commandLine = $"cmd.exe /d /k \"title {title}{(string.IsNullOrWhiteSpace(command) ? "" : " & " + command)}\"";
        var startup = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>() };
        vars = new Dictionary<string, string>(vars)
        {
            ["CSHARPFAR_HIDDEN_RESIZE_TRACE"] = Path.Combine(path, "trace.log"),
        };
        IntPtr environment = Native.CreateEnvironmentBlock(vars);
        try
        {
            if (!Native.CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    Native.CreateNewConsole | Native.CreateUnicodeEnvironment, environment, Directory.GetCurrentDirectory(), ref startup, out var processInfo))
                throw new InvalidOperationException($"Could not start cmd.exe: {Marshal.GetLastWin32Error()}.");
            Native.CloseHandle(processInfo.hThread);
            Native.CloseHandle(processInfo.hProcess);
            var cmd = Process.GetProcessById((int)processInfo.dwProcessId);
            return new ConsoleSession(cmd, path);
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }
    }
    public void WaitForConsole()
    {
        for (int i = 0; i < 100; i++)
        {
            Native.FreeConsole();
            if (Native.AttachConsole((uint)_cmd.Id))
            {
                _output = Native.GetStdHandle(-11); _input = Native.GetStdHandle(-10); _hwnd = Native.GetConsoleWindow();
                if (_output != IntPtr.Zero && _output != new IntPtr(-1)) return;
            }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Could not attach to child conhost console.");
    }
    public void SendText(string text) { foreach (char c in text) Key(c, 0, 0); }
    public void Enter() => Key('\r', 0x0D, 0);
    public void CtrlO() => Key('o', 0x4F, 0x0008);
    public void ScrollUp() => Key('\0', 0x21, 0);
    private void Key(char c, ushort vk, uint control)
    {
        var down = new Native.InputRecord { EventType = 1, Key = new Native.KeyEventRecord { KeyDown = true, RepeatCount = 1, VirtualKeyCode = vk, UnicodeChar = c, ControlKeyState = control } };
        var up = new Native.InputRecord { EventType = 1, Key = new Native.KeyEventRecord { KeyDown = false, RepeatCount = 1, VirtualKeyCode = vk, UnicodeChar = c, ControlKeyState = control } };
        Native.WriteConsoleInput(_input, [down, up], 2, out _);
    }
    public int WindowWidth { get { Native.GetConsoleScreenBufferInfo(_output, out var info); return info.Window.Right - info.Window.Left + 1; } }

    public void Resize(int width, int rows)
    {
        Native.GetConsoleScreenBufferInfo(_output, out var info); int buffer = Math.Max(300, (int)info.Size.Y);
        int oldWidth = info.Window.Right - info.Window.Left + 1;
        int oldHeight = info.Window.Bottom - info.Window.Top + 1;
        var viewport = new Native.SmallRect(0, (short)(buffer - rows), (short)(width - 1), (short)(buffer - 1));
        if (width < oldWidth || rows < oldHeight)
        {
            var shrinkingViewport = new Native.SmallRect(
                info.Window.Left,
                info.Window.Top,
                (short)(info.Window.Left + width - 1),
                (short)(info.Window.Top + rows - 1));
            Native.SetConsoleWindowInfo(_output, true, ref shrinkingViewport);
        }
        Native.SetConsoleScreenBufferSize(_output, new Native.Coord((short)width, (short)buffer));
        EnsureConsoleApi(Native.SetConsoleWindowInfo(_output, true, ref viewport), "Could not set console viewport");
    }

    private static void EnsureConsoleApi(bool success, string operation)
    {
        if (!success)
            throw new InvalidOperationException($"{operation}: {Marshal.GetLastWin32Error()}.");
    }
    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);
    public bool WaitForText(string text, int timeout) { var until = Environment.TickCount64 + timeout; while (Environment.TickCount64 < until) { if (Dump().Contains(text, StringComparison.Ordinal)) return true; Thread.Sleep(100); } return false; }
    public void Capture(string phase)
    {
        Native.GetConsoleScreenBufferInfo(_output, out var info); string screen = Dump(info); File.WriteAllText(Path.Combine(_path, phase + ".screen.txt"), screen);
        SavePng(Path.Combine(_path, phase + ".png"));
        File.WriteAllText(Path.Combine(_path, phase + ".relevant-buffer-range.txt"), DumpBuffer());
        int viewportHeight = info.Window.Bottom - info.Window.Top + 1;
        _states.Add(new CaptureState(phase, info.Size.X, info.Size.Y, info.CursorPosition.X, info.CursorPosition.Y, info.Window.Left, info.Window.Top, info.Window.Right, info.Window.Bottom, viewportHeight, info.Size.Y - viewportHeight, info.Window.Bottom >= info.Size.Y - 1));
        File.WriteAllText(Path.Combine(_path, "state.json"), JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true }));
    }
    public PromptCapture CapturePrompt(string phase, string prompt)
    {
        Capture(phase);
        var (_, width, cells) = ReadBuffer();
        Native.GetConsoleScreenBufferInfo(_output, out var info);
        var result = PromptDetector.Analyze(cells, width, prompt, info.Window) with { Phase = phase };
        File.WriteAllText(Path.Combine(_path, phase + ".prompts.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
    public string ReadTrace() => File.Exists(_trace) ? File.ReadAllText(_trace) : string.Empty;
    public bool ViewportAtBottom
    {
        get
        {
            Native.GetConsoleScreenBufferInfo(_output, out var info);
            return info.Window.Bottom >= info.Size.Y - 1;
        }
    }
    public bool TryScrollViewportUp()
    {
        Native.GetConsoleScreenBufferInfo(_output, out var info);
        int height = info.Window.Bottom - info.Window.Top + 1;
        if (info.Size.Y <= height || info.Window.Top == 0)
            return false;
        int top = Math.Max(0, (int)info.Window.Top - Math.Min(5, (int)info.Window.Top));
        var target = new Native.SmallRect(info.Window.Left, (short)top, info.Window.Right, (short)(top + height - 1));
        return Native.SetConsoleWindowInfo(_output, true, ref target) && !ViewportAtBottom;
    }
    private string Dump() { Native.GetConsoleScreenBufferInfo(_output, out var info); return Dump(info); }
    private string DumpBuffer()
    {
        Native.GetConsoleScreenBufferInfo(_output, out var info);
        int w = info.Size.X, h = info.Size.Y; var cells = new Native.CharInfo[w * h]; var region = new Native.SmallRect(0, 0, (short)(w - 1), (short)(h - 1));
        if (!Native.ReadConsoleOutput(_output, cells, new Native.Coord((short)w, (short)h), new Native.Coord(0, 0), ref region)) return "";
        return string.Join(Environment.NewLine, Enumerable.Range(0, h).Select(y => new string(cells.Skip(y * w).Take(w).Select(c => c.UnicodeChar == '\0' ? ' ' : c.UnicodeChar).ToArray()).TrimEnd()));
    }
    private string Dump(Native.ConsoleScreenBufferInfo info)
    {
        int w = info.Window.Right - info.Window.Left + 1, h = info.Window.Bottom - info.Window.Top + 1; var cells = new Native.CharInfo[w * h]; var region = info.Window;
        if (!Native.ReadConsoleOutput(_output, cells, new Native.Coord((short)w, (short)h), new Native.Coord(0, 0), ref region)) return "";
        return string.Join(Environment.NewLine, Enumerable.Range(0, h).Select(y => new string(cells.Skip(y * w).Take(w).Select(c => c.UnicodeChar == '\0' ? ' ' : c.UnicodeChar).ToArray()).TrimEnd()));
    }
    private void SavePng(string file) { if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return; using var b = new Bitmap(r.Right - r.Left, r.Bottom - r.Top); using var g = Graphics.FromImage(b); g.CopyFromScreen(r.Left, r.Top, 0, 0, b.Size); b.Save(file, ImageFormat.Png); }
    public VariantResult Result(string variant, string scenario, int expectedRun)
    {
        var (all, width, cells) = ReadBuffer();
        File.WriteAllText(Path.Combine(_path, "final.buffer.txt"), all);
        var analysis = MarkerAnalysis.Analyze(cells, width, expectedRun);
        string trace = File.Exists(_trace) ? File.ReadAllText(_trace) : "";
        int capturedFull = Regex.Matches(trace, @"WRITE type=Win32FrameBatch ownership=CapturedExternalSurface").Count;
        int full = Regex.Matches(trace, @"type=Win32FrameBatch").Count;
        var prompts = MarkerAnalysis.FindPrompts(Dump());
        bool valid = capturedFull == 0;
        bool corrupt = analysis.Misaligned > 0 || analysis.Fragments > 0 || analysis.MultiMarkerRows > 0 || analysis.Missing.Count > 0 || analysis.Duplicates > 0;
        var result = new VariantResult($"{variant}/{scenario}", true, valid, corrupt, prompts.Count > 1, analysis.Missing.Count, analysis.Duplicates, full, capturedFull, prompts, analysis.Fragments, analysis.Misaligned, analysis.Offsets);
        File.WriteAllText(Path.Combine(_path, "summary.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
    private (string Text, int Width, Native.CharInfo[] Cells) ReadBuffer()
    {
        Native.GetConsoleScreenBufferInfo(_output, out var info); int w = info.Size.X, h = info.Size.Y; var cells = new Native.CharInfo[w * h]; var region = new Native.SmallRect(0, 0, (short)(w - 1), (short)(h - 1));
        if (!Native.ReadConsoleOutput(_output, cells, new Native.Coord((short)w, (short)h), new Native.Coord(0, 0), ref region)) return ("", 0, []);
        return (string.Join(Environment.NewLine, Enumerable.Range(0, h).Select(y => new string(cells.Skip(y * w).Take(w).Select(c => c.UnicodeChar == '\0' ? ' ' : c.UnicodeChar).ToArray()).TrimEnd())), w, cells);
    }
    public void Dispose()
    {
        try { if (!_cmd.HasExited) _cmd.Kill(true); } catch { }
        Native.FreeConsole();
    }
}

sealed record CaptureState(string Phase, int BufferWidth, int BufferHeight, int CursorX, int CursorY, int Left, int Top, int Right, int Bottom, int ViewportHeight, int ScrollbackRows, bool ViewportAtBottom);
sealed record MarkerOffset(string Marker, int X, int Y);
sealed record VariantResult(string Name, bool Completed, bool Valid, bool Corruption, bool DuplicatePrompt, int MissingMarkers, int DuplicateMarkers, int FullViewportWrites, int CapturedFullViewportWrites, IReadOnlyList<int> PromptRows, int FragmentCount, int MisalignedMarkerCount, IReadOnlyList<MarkerOffset> MarkerOffsets);

sealed record PromptVariant(
    string Name,
    Dictionary<string, string> Variables,
    int Cycles);

sealed record PromptLocation(int Row, int X, string Kind, bool Visible);

sealed record PromptCapture(
    string Phase,
    IReadOnlyList<PromptLocation> Prompts,
    IReadOnlyList<PromptLocation> Fragments);

sealed record ScrollResult(bool Valid, bool ReturnedToBottomOnInput, bool OnePromptAfterInput)
{
    public static ScrollResult Invalid { get; } = new(false, false, false);
}

sealed record PromptVariantResult(
    string Name,
    bool Valid,
    bool DuplicatePrompt,
    int PromptCount,
    IReadOnlyList<PromptLocation> Prompts,
    IReadOnlyList<PromptLocation> Fragments,
    int CapturedFullViewportWrites,
    ScrollResult Scroll,
    IReadOnlyList<PromptCapture> Captures,
    VariantResult History,
    string? Error)
{
    public static PromptVariantResult Invalid(string name, string error) =>
        new(name, false, false, 0, [], [], 0, ScrollResult.Invalid, [],
            new VariantResult(name, false, false, true, false, 120, 0, 0, 0, [], 0, 0, []), error);
}

static class PromptDetector
{
    public static PromptCapture Analyze(
        Native.CharInfo[] cells,
        int width,
        string prompt,
        Native.SmallRect viewport)
    {
        var prompts = new List<PromptLocation>();
        var fragments = new List<PromptLocation>();
        if (width <= 0 || string.IsNullOrEmpty(prompt))
            return new PromptCapture("", prompts, fragments);

        int height = cells.Length / width;
        string directory = prompt[..^1];
        for (int row = 0; row < height; row++)
        {
            string line = new(cells.Skip(row * width).Take(width).Select(cell => cell.UnicodeChar == '\0' ? ' ' : cell.UnicodeChar).ToArray());
            int start = 0;
            while ((start = line.IndexOf(prompt, start, StringComparison.Ordinal)) >= 0)
            {
                prompts.Add(new PromptLocation(row, start, "full", IsVisible(row, start, viewport)));
                start += prompt.Length;
            }

            int partial = line.IndexOf(directory, StringComparison.Ordinal);
            if (partial >= 0 && !line.AsSpan(partial).StartsWith(prompt, StringComparison.Ordinal) &&
                row + 1 < height && RowStartsWith(cells, width, row + 1, ">"))
            {
                fragments.Add(new PromptLocation(row, partial, "split", IsVisible(row, partial, viewport)));
            }
        }
        return new PromptCapture("", prompts, fragments);
    }

    private static bool RowStartsWith(Native.CharInfo[] cells, int width, int row, string text)
    {
        if (text.Length > width)
            return false;
        for (int x = 0; x < text.Length; x++)
            if (cells[row * width + x].UnicodeChar != text[x])
                return false;
        return true;
    }

    private static bool IsVisible(int row, int x, Native.SmallRect viewport) =>
        row >= viewport.Top && row <= viewport.Bottom && x >= viewport.Left && x <= viewport.Right;
}

static class MarkerAnalysis
{
    public static (List<int> Missing, int Duplicates, int Fragments, int Misaligned, int MultiMarkerRows, List<MarkerOffset> Offsets) Analyze(Native.CharInfo[] cells, int width, int run)
    {
        var found = new Dictionary<int, int>(); var offsets = new List<MarkerOffset>(); int fragments = 0, misaligned = 0, multi = 0;
        string markerPattern = run == 0 ? @"LINE_0*(\d+)" : @"RUN_\d{2}_LINE_0*(\d+)";
        for (int y = 0; y < cells.Length / width; y++)
        {
            string row = new(cells.Skip(y * width).Take(width).Select(c => c.UnicodeChar == '\0' ? ' ' : c.UnicodeChar).ToArray());
            MatchCollection matches = Regex.Matches(row, markerPattern);
            if (matches.Count > 1) multi++;
            foreach (Match match in matches)
            {
                int n = int.Parse(match.Groups[1].Value);
                if (n is < 1 or > 120)
                    continue;
                found[n] = found.GetValueOrDefault(n) + 1;
                offsets.Add(new MarkerOffset(n.ToString(), match.Index, y));
                if (match.Index != 0) misaligned++;
            }
            string withoutFull = Regex.Replace(row, markerPattern, "");
            if (Regex.IsMatch(withoutFull, @"(?<![A-Z_])(?:LINE_)?0{2,}\d+")) fragments++;
        }
        return (Enumerable.Range(1, 120).Where(n => !found.ContainsKey(n)).ToList(), found.Values.Sum(x => Math.Max(0, x - 1)), fragments, misaligned, multi, offsets);
    }
    public static List<int> FindPrompts(string visible) => visible.Split(Environment.NewLine).Select((row, i) => (row, i)).Where(x => Regex.IsMatch(x.row, @"[A-Za-z]:\\[^>]*>")).Select(x => x.i).ToList();
}

static class Native
{
    public const uint CreateNewConsole = 0x00000010;
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var values = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => (string?)x.Value ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
            values[key] = value;
        string block = string.Join('\0', values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value}")) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct StartupInfo { public int cb; public string? lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)] public struct ProcessInformation { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)] public struct Coord { public short X, Y; public Coord(short x, short y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SmallRect { public short Left, Top, Right, Bottom; public SmallRect(short l, short t, short r, short b) { Left = l; Top = t; Right = r; Bottom = b; } }
    [StructLayout(LayoutKind.Sequential)] public struct CharInfo { public char UnicodeChar; public short Attributes; }
    [StructLayout(LayoutKind.Sequential)] public struct ConsoleScreenBufferInfo { public Coord Size, CursorPosition; public short Attributes; public SmallRect Window; public Coord MaximumWindowSize; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyEventRecord { [MarshalAs(UnmanagedType.Bool)] public bool KeyDown; public ushort RepeatCount, VirtualKeyCode, VirtualScanCode; public char UnicodeChar; public uint ControlKeyState; }
    [StructLayout(LayoutKind.Explicit)] public struct InputRecord { [FieldOffset(0)] public ushort EventType; [FieldOffset(4)] public KeyEventRecord Key; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetConsoleScreenBufferInfo(IntPtr h, out ConsoleScreenBufferInfo i);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadConsoleOutput(IntPtr h, [Out] CharInfo[] b, Coord bs, Coord bc, ref SmallRect r);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteConsoleInput(IntPtr h, InputRecord[] r, int n, out int written);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleScreenBufferSize(IntPtr h, Coord s);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleWindowInfo(IntPtr h, bool absolute, ref SmallRect r);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
