using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console.Ansi;

public sealed class AnsiTerminalConsoleDriver : IConsoleDriver, ITerminalScreenMode, IConsoleOutputModeDriver, IDisposable
{
    private const string ClearScreen = "\x1b[2J";
    private const string CursorHome = "\x1b[H";
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursor = "\x1b[?25h";
    private const string EnterAltScreen = "\x1b[?1049h";
    private const string LeaveAltScreen = "\x1b[?1049l";
    private const string ResetAttributes = "\x1b[0m";
    private UnixTerminalMode? _diagnosticTerminalMode;
    private readonly UnixTerminalInputByteReader _diagnosticInput;
    private readonly AnsiInputParser _inputParser = new();
    private readonly IConsoleInputReader _inputReader;
    private bool _applicationScreenActive;
    private ConsoleColor? _currentForeground;
    private ConsoleColor? _currentBackground;
    private TextAttributes _currentAttributes;
    private readonly AnsiCursorPositionCache _cursorPosition = new();
    private bool? _cursorVisible;
    private bool _disposed;

    public AnsiTerminalConsoleDriver()
        : this(inputReader: null)
    {
    }

    internal AnsiTerminalConsoleDriver(IConsoleInputReader? inputReader)
    {
        if (global::System.Console.IsInputRedirected || global::System.Console.IsOutputRedirected)
            throw new InvalidOperationException("Ansi terminal console driver requires attached stdin and stdout terminals.");

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        _diagnosticInput = new UnixTerminalInputByteReader();
        _inputReader = inputReader ?? new UnixRawTerminalInputReader(
            new UnixTerminalInputByteReader(),
            GetSize,
            ResetCachedState,
            WriteControl);
    }

    public bool IsSupported => true;

    public bool IsApplicationScreenActive => _applicationScreenActive;

    public ConsoleViewport GetViewport() => new(0, 0, GetBufferWidth(), GetBufferHeight());

    public ConsoleSize GetSize() => GetViewport().Size;

    public bool TryScrollViewportToBottom() => false;

    public string InputBackendName => _inputReader.BackendName;

    public bool IsMouseTrackingEnabled => _inputReader.MouseTrackingEnabled;

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default) =>
        _inputReader.ReadInput(intercept, cancellationToken);

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent) =>
        _inputReader.TryReadInput(intercept, out inputEvent);

    public ConsoleKeyInfo ReadKey(bool intercept) => _inputReader.ReadKey(intercept);

    public AnsiInputReadResult ReadRawInput()
    {
        EnableRawInputMode();
        return _inputParser.Read(_diagnosticInput);
    }

    public AnsiInputReadResult ReadRawInput(int escapeTimeoutMilliseconds)
    {
        EnableRawInputMode();
        return new AnsiInputParser(escapeTimeoutMilliseconds).Read(_diagnosticInput);
    }

    public bool TryReadRawInput(int timeoutMilliseconds, [NotNullWhen(true)] out AnsiInputReadResult? result)
    {
        EnableRawInputMode();
        if (!_diagnosticInput.WaitForInput(timeoutMilliseconds))
        {
            result = null;
            return false;
        }

        result = _inputParser.Read(_diagnosticInput);
        return true;
    }

    public bool TryReadRawInput(
        int timeoutMilliseconds,
        int escapeTimeoutMilliseconds,
        [NotNullWhen(true)] out AnsiInputReadResult? result)
    {
        EnableRawInputMode();
        if (!_diagnosticInput.WaitForInput(timeoutMilliseconds))
        {
            result = null;
            return false;
        }

        result = new AnsiInputParser(escapeTimeoutMilliseconds).Read(_diagnosticInput);
        return true;
    }

    public void EnableRawInputMode() =>
        _diagnosticTerminalMode ??= new UnixTerminalMode();

    public void WriteRawControl(string sequence) =>
        WriteControl(sequence);

    public void WriteAt(
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null,
        TextAttributes attributes = TextAttributes.None)
    {
        int column = Math.Max(0, x);
        int row = Math.Max(0, y);
        SetCursorPosition(column, row);
        ApplyStyle(foreground ?? ConsoleColor.Gray, background ?? ConsoleColor.Black, attributes);
        global::System.Console.Out.Write(text);
        _cursorPosition.TrackWrite(column, row, text.Length, GetBufferWidth());
    }

    public bool TryWriteAtViewport(
        ConsoleViewport viewport,
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null,
        TextAttributes attributes = TextAttributes.None)
    {
        WriteAt(viewport.Left + x, viewport.Top + y, text, foreground, background, attributes);
        return true;
    }

    public void ClearRegion(Rect region)
    {
        string blank = new(' ', Math.Max(0, region.Width));
        for (int y = region.Y; y < region.Bottom; y++)
            WriteAt(region.X, y, blank);
    }

    public void SetCursorPosition(int x, int y)
    {
        int column = Math.Max(0, x);
        int row = Math.Max(0, y);
        if (_cursorPosition.IsAt(column, row))
            return;

        WriteControl($"\x1b[{row + 1};{column + 1}H");
        _cursorPosition.Set(column, row);
    }

    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        SetCursorPosition(viewport.Left + x, viewport.Top + y);
        return true;
    }

    public void SetCursorVisible(bool visible)
    {
        if (!visible)
        {
            WriteControl(HideCursor);
            _cursorVisible = false;
            return;
        }

        if (_cursorVisible == visible)
            return;

        WriteControl(ShowCursor);
        _cursorVisible = visible;
    }

    public ScreenSnapshot Capture(Rect region) =>
        new(GetViewport(), region, new SnapshotCell[Math.Max(0, region.Height), Math.Max(0, region.Width)]);

    public void Restore(ScreenSnapshot snapshot)
    {
    }

    public void Clear() => WriteControl(ClearScreen + CursorHome);

    public void Write(string text)
    {
        global::System.Console.Out.Write(text);
        _cursorPosition.Reset();
    }

    public int GetBufferWidth() => Math.Max(1, global::System.Console.WindowWidth);

    public int GetBufferHeight() => Math.Max(1, global::System.Console.WindowHeight);

    public void EnterAlternateScreen() => EnterApplicationScreen();

    public void LeaveAlternateScreen() => LeaveApplicationScreen();

    public void EnterApplicationScreen()
    {
        if (_applicationScreenActive)
            return;

        WriteControl(EnterAltScreen + ClearScreen + CursorHome);
        ResetCachedState();
        _applicationScreenActive = true;
    }

    public void LeaveApplicationScreen()
    {
        if (!_applicationScreenActive)
            return;

        WriteControl(ResetAttributes + ShowCursor + LeaveAltScreen);
        ResetCachedState();
        _cursorVisible = true;
        _applicationScreenActive = false;
    }

    public void EnsureApplicationScreen() => EnterApplicationScreen();

    public void EnsureMainScreen() => LeaveApplicationScreen();

    public void SetRenderingOutputMode(bool enabled)
    {
    }

    public void SetConsoleScrollbackEnabled(bool enabled)
    {
    }

    public void RestoreApplicationInputMode()
    {
        _inputReader.RestoreInputMode();
        _diagnosticTerminalMode?.EnableRawMode();
    }

    public IDisposable EnterChildProcessConsoleMode()
    {
        _inputReader.SuspendInputMode();
        _diagnosticTerminalMode?.RestoreOriginalMode();
        WriteControl(ResetAttributes + ShowCursor);
        _cursorVisible = true;
        EnsureMainScreen();
        Flush();
        return new ChildProcessConsoleModeScope(this);
    }

    public void RestoreTerminal()
    {
        try
        {
            WriteControl(LeaveAltScreen + ShowCursor + ResetAttributes);
        }
        finally
        {
            ResetCachedState();
            _cursorVisible = true;
            _applicationScreenActive = false;
            try
            {
                _inputReader.SuspendInputMode();
            }
            finally
            {
                _diagnosticTerminalMode?.RestoreOriginalMode();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            RestoreTerminal();
        }
        finally
        {
            try
            {
                _inputReader.Dispose();
            }
            finally
            {
                _diagnosticTerminalMode?.Dispose();
                _disposed = true;
            }
        }
    }

    private static void WriteControl(string sequence)
    {
        global::System.Console.Out.Write(sequence);
        global::System.Console.Out.Flush();
    }

    private static void Flush() => global::System.Console.Out.Flush();

    private void ApplyStyle(ConsoleColor foreground, ConsoleColor background, TextAttributes attributes)
    {
        if (_currentForeground == foreground &&
            _currentBackground == background &&
            _currentAttributes == attributes)
        {
            return;
        }

        WriteControl(AnsiStyleSequences.BuildSgr(foreground, background, attributes));
        _currentForeground = foreground;
        _currentBackground = background;
        _currentAttributes = attributes;
    }

    private void ResetCachedState()
    {
        _currentForeground = null;
        _currentBackground = null;
        _currentAttributes = TextAttributes.None;
        _cursorPosition.Reset();
    }

    private sealed class ChildProcessConsoleModeScope : IDisposable
    {
        private readonly AnsiTerminalConsoleDriver _owner;
        private bool _disposed;

        public ChildProcessConsoleModeScope(AnsiTerminalConsoleDriver owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _owner.RestoreApplicationInputMode();
            _disposed = true;
        }
    }

}
