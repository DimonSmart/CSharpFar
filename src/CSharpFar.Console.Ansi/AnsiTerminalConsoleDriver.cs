using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console.Ansi;

public sealed class AnsiTerminalConsoleDriver : IConsoleDriver, ITerminalScreenMode, IDisposable
{
    private const string ClearScreen = "\x1b[2J";
    private const string CursorHome = "\x1b[H";
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursor = "\x1b[?25h";
    private const string EnterAltScreen = "\x1b[?1049h";
    private const string LeaveAltScreen = "\x1b[?1049l";
    private const string ResetAttributes = "\x1b[0m";

    private readonly UnixTerminalMode _terminalMode;
    private readonly Stream _input;
    private readonly AnsiInputParser _inputParser = new();
    private bool _applicationScreenActive;
    private bool _disposed;

    public AnsiTerminalConsoleDriver()
    {
        if (global::System.Console.IsInputRedirected || global::System.Console.IsOutputRedirected)
            throw new InvalidOperationException("Ansi terminal console driver requires attached stdin and stdout terminals.");

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        _terminalMode = new UnixTerminalMode();
        _input = global::System.Console.OpenStandardInput();
    }

    public bool IsSupported => true;

    public bool IsApplicationScreenActive => _applicationScreenActive;

    public ConsoleViewport GetViewport() => new(0, 0, GetBufferWidth(), GetBufferHeight());

    public ConsoleSize GetSize() => GetViewport().Size;

    public bool TryScrollViewportToBottom() => false;

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default) =>
        new KeyConsoleInputEvent(ReadKey(intercept));

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        inputEvent = null;
        if (!global::System.Console.KeyAvailable)
            return false;

        inputEvent = new KeyConsoleInputEvent(ReadKey(intercept));
        return true;
    }

    public ConsoleKeyInfo ReadKey(bool intercept) => _inputParser.ReadKey(_input);

    public void WriteAt(
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null)
    {
        SetCursorPosition(x, y);
        global::System.Console.Out.Write(text);
    }

    public bool TryWriteAtViewport(
        ConsoleViewport viewport,
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null)
    {
        WriteAt(viewport.Left + x, viewport.Top + y, text, foreground, background);
        return true;
    }

    public void ClearRegion(Rect region)
    {
        string blank = new(' ', Math.Max(0, region.Width));
        for (int y = region.Y; y < region.Bottom; y++)
            WriteAt(region.X, y, blank);
    }

    public void SetCursorPosition(int x, int y) =>
        WriteControl($"\x1b[{Math.Max(1, y + 1)};{Math.Max(1, x + 1)}H");

    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        SetCursorPosition(viewport.Left + x, viewport.Top + y);
        return true;
    }

    public void SetCursorVisible(bool visible) => WriteControl(visible ? ShowCursor : HideCursor);

    public ScreenSnapshot Capture(Rect region) =>
        new(GetViewport(), region, new SnapshotCell[Math.Max(0, region.Height), Math.Max(0, region.Width)]);

    public void Restore(ScreenSnapshot snapshot)
    {
    }

    public void Clear() => WriteControl(ClearScreen + CursorHome);

    public void Write(string text) => global::System.Console.Out.Write(text);

    public int GetBufferWidth() => global::System.Console.WindowWidth;

    public int GetBufferHeight() => global::System.Console.WindowHeight;

    public void EnterAlternateScreen() => EnterApplicationScreen();

    public void LeaveAlternateScreen() => LeaveApplicationScreen();

    public void EnterApplicationScreen()
    {
        if (_applicationScreenActive)
            return;

        WriteControl(EnterAltScreen + ClearScreen + CursorHome);
        _applicationScreenActive = true;
    }

    public void LeaveApplicationScreen()
    {
        if (!_applicationScreenActive)
            return;

        WriteControl(LeaveAltScreen);
        _applicationScreenActive = false;
    }

    public void EnsureApplicationScreen() => EnterApplicationScreen();

    public void EnsureMainScreen() => LeaveApplicationScreen();

    public void RestoreTerminal()
    {
        WriteControl(LeaveAltScreen + ShowCursor + ResetAttributes);
        _applicationScreenActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RestoreTerminal();
        _terminalMode.Dispose();
        _input.Dispose();
        _disposed = true;
    }

    private static void WriteControl(string sequence)
    {
        global::System.Console.Out.Write(sequence);
        global::System.Console.Out.Flush();
    }
}
