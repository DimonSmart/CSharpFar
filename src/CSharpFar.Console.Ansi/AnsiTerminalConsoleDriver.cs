using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
    private UnixTerminalMode? _terminalMode;
    private readonly UnixTerminalInputByteReader _input;
    private readonly AnsiInputParser _inputParser = new();
    private ConsoleSize _lastKnownSize;
    private bool _applicationScreenActive;
    private ConsoleColor? _currentForeground;
    private ConsoleColor? _currentBackground;
    private TextAttributes _currentAttributes;
    private int _cursorX = -1;
    private int _cursorY = -1;
    private bool? _cursorVisible;
    private bool _disposed;

    public AnsiTerminalConsoleDriver()
    {
        if (global::System.Console.IsInputRedirected || global::System.Console.IsOutputRedirected)
            throw new InvalidOperationException("Ansi terminal console driver requires attached stdin and stdout terminals.");

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        _input = new UnixTerminalInputByteReader();
        _lastKnownSize = GetSize();
    }

    public bool IsSupported => true;

    public bool IsApplicationScreenActive => _applicationScreenActive;

    public ConsoleViewport GetViewport() => new(0, 0, GetBufferWidth(), GetBufferHeight());

    public ConsoleSize GetSize() => GetViewport().Size;

    public bool TryScrollViewportToBottom() => false;

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryReadResize(out var resize))
            return resize;

        return new KeyConsoleInputEvent(ReadKey(intercept));
    }

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        if (TryReadResize(out inputEvent))
            return true;

        inputEvent = null;
        if (!global::System.Console.KeyAvailable)
            return false;

        inputEvent = new KeyConsoleInputEvent(ReadKey(intercept));
        return true;
    }

    public ConsoleKeyInfo ReadKey(bool intercept) =>
        global::System.Console.ReadKey(intercept);

    public AnsiInputReadResult ReadRawInput()
    {
        _terminalMode ??= new UnixTerminalMode();
        return _inputParser.Read(_input);
    }

    public void WriteAt(
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null,
        TextAttributes attributes = TextAttributes.None)
    {
        SetCursorPosition(x, y);
        ApplyStyle(foreground ?? ConsoleColor.Gray, background ?? ConsoleColor.Black, attributes);
        global::System.Console.Out.Write(text);
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
        if (_cursorX == column && _cursorY == row)
            return;

        WriteControl($"\x1b[{row + 1};{column + 1}H");
        _cursorX = column;
        _cursorY = row;
    }

    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        SetCursorPosition(viewport.Left + x, viewport.Top + y);
        return true;
    }

    public void SetCursorVisible(bool visible)
    {
        if (_cursorVisible == visible)
            return;

        WriteControl(visible ? ShowCursor : HideCursor);
        _cursorVisible = visible;
    }

    public ScreenSnapshot Capture(Rect region) =>
        new(GetViewport(), region, new SnapshotCell[Math.Max(0, region.Height), Math.Max(0, region.Width)]);

    public void Restore(ScreenSnapshot snapshot)
    {
    }

    public void Clear() => WriteControl(ClearScreen + CursorHome);

    public void Write(string text) => global::System.Console.Out.Write(text);

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

    public void RestoreApplicationInputMode() => _terminalMode?.EnableRawMode();

    public IDisposable EnterChildProcessConsoleMode()
    {
        WriteControl(ResetAttributes + ShowCursor);
        _cursorVisible = true;
        EnsureMainScreen();
        _terminalMode?.RestoreOriginalMode();
        Flush();
        return new ChildProcessConsoleModeScope(this);
    }

    public void RestoreTerminal()
    {
        WriteControl(LeaveAltScreen + ShowCursor + ResetAttributes);
        ResetCachedState();
        _cursorVisible = true;
        _applicationScreenActive = false;
        _terminalMode?.RestoreOriginalMode();
        Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RestoreTerminal();
        _terminalMode?.Dispose();
        _disposed = true;
    }

    private static void WriteControl(string sequence)
    {
        global::System.Console.Out.Write(sequence);
        global::System.Console.Out.Flush();
    }

    private static void Flush() => global::System.Console.Out.Flush();

    private bool TryReadResize([NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        var size = GetSize();
        if (size.Width == _lastKnownSize.Width && size.Height == _lastKnownSize.Height)
        {
            inputEvent = null;
            return false;
        }

        _lastKnownSize = size;
        ResetCachedState();
        inputEvent = new ConsoleResizeInputEvent();
        return true;
    }

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
        _cursorX = -1;
        _cursorY = -1;
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

    private sealed class UnixTerminalInputByteReader : IAnsiInputByteReader
    {
        private const int STDIN_FILENO = 0;
        private const short POLLIN = 0x0001;
        private const int PacketIdleTimeoutMilliseconds = 100;

        private readonly Queue<byte> _pending = new();

        public byte ReadByte()
        {
            while (_pending.Count == 0)
                ReadPacket(block: true);

            return _pending.Dequeue();
        }

        public bool TryReadByte(out byte value)
        {
            if (_pending.Count == 0 && !ReadPacket(block: false))
            {
                value = default;
                return false;
            }

            value = _pending.Dequeue();
            return true;
        }

        public bool WaitForInput(int timeoutMilliseconds)
        {
            if (_pending.Count > 0)
                return true;

            return PollInput(timeoutMilliseconds);
        }

        private bool ReadPacket(bool block)
        {
            if (!PollInput(block ? -1 : 0))
                return false;

            do
            {
                byte[] buffer = new byte[32];
                int readCount = ReadInto(buffer);
                if (readCount < 0)
                    throw new InvalidOperationException("Failed to read terminal input.", new Win32Exception(Marshal.GetLastPInvokeError()));

                for (int i = 0; i < readCount; i++)
                    _pending.Enqueue(buffer[i]);
            }
            while (PollInput(PacketIdleTimeoutMilliseconds));

            return _pending.Count > 0;
        }

        private static bool PollInput(int timeoutMilliseconds)
        {
            var fds = new[] { new PollFd { Fd = STDIN_FILENO, Events = POLLIN } };
            int result = poll(fds, 1, timeoutMilliseconds);
            if (result < 0)
                throw new InvalidOperationException("Failed to poll terminal input.", new Win32Exception(Marshal.GetLastPInvokeError()));

            return result > 0 && (fds[0].Revents & POLLIN) != 0;
        }

        private static int ReadInto(byte[] buffer)
        {
            IntPtr nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
            try
            {
                nint readCount = read(STDIN_FILENO, nativeBuffer, (nuint)buffer.Length);
                if (readCount > 0)
                    Marshal.Copy(nativeBuffer, buffer, 0, (int)readCount);

                return (int)readCount;
            }
            finally
            {
                Marshal.FreeHGlobal(nativeBuffer);
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int poll(PollFd[] fds, nuint nfds, int timeout);

        [DllImport("libc", SetLastError = true)]
        private static extern nint read(int fd, IntPtr buffer, nuint count);

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }
    }
}
