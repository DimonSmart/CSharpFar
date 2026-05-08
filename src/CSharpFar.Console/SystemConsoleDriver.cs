using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Console.Win32;

namespace CSharpFar.Console;

/// <summary>
/// Real console driver backed by System.Console.
/// On Windows, Capture/Restore use Win32 ReadConsoleOutput/WriteConsoleOutput
/// so that shell output underneath the panels is preserved for Ctrl+O.
/// This class targets Windows. On other platforms, Capture/Restore use a blank fallback.
/// </summary>
public sealed class SystemConsoleDriver : IConsoleDriver, IConsoleOutputModeDriver, IDisposable
{
    private readonly IntPtr _consoleHandle;
    private readonly IntPtr _inputHandle;
    private readonly uint _originalInputMode;
    private readonly uint _originalOutputMode;
    private readonly bool _restoreInputMode;
    private readonly bool _restoreOutputMode;
    private ConsoleSize _lastInputSize;
    private bool _renderingOutputMode;
    private bool _disposed;

    public SystemConsoleDriver()
    {
        if (OperatingSystem.IsWindows())
        {
            _consoleHandle = Win32ConsoleApi.GetConsoleOutputHandle();
            _inputHandle = Win32ConsoleApi.GetConsoleInputHandle();
            _restoreInputMode = TryConfigureInputMode(_inputHandle, out _originalInputMode);
            _restoreOutputMode = Win32ConsoleApi.TryGetConsoleMode(_consoleHandle, out _originalOutputMode);
        }

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        _lastInputSize = GetSize();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (OperatingSystem.IsWindows() && _restoreInputMode)
            Win32ConsoleApi.TrySetConsoleMode(_inputHandle, _originalInputMode);
        if (OperatingSystem.IsWindows() && _restoreOutputMode)
            Win32ConsoleApi.TrySetConsoleMode(_consoleHandle, _originalOutputMode);

        _disposed = true;
    }

    public void SetRenderingOutputMode(bool enabled)
    {
        if (!OperatingSystem.IsWindows() || !_restoreOutputMode || _renderingOutputMode == enabled)
            return;

        uint mode = enabled
            ? _originalOutputMode & ~Win32ConsoleApi.ENABLE_WRAP_AT_EOL_OUTPUT
            : _originalOutputMode;

        if (Win32ConsoleApi.TrySetConsoleMode(_consoleHandle, mode))
            _renderingOutputMode = enabled;
    }

    public ConsoleSize GetSize() =>
        new(global::System.Console.WindowWidth, global::System.Console.WindowHeight);

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            var inputEvent = Win32ConsoleApi.ReadInput(
                _inputHandle,
                intercept,
                cancellationToken,
                HasVisibleWindowSizeChanged);

            if (inputEvent is ConsoleResizeInputEvent)
                _lastInputSize = GetSize();

            return inputEvent;
        }

        // Non-Windows fallback: key-only
        cancellationToken.ThrowIfCancellationRequested();
        var key = global::System.Console.ReadKey(intercept);
        return new KeyConsoleInputEvent(key);
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (OperatingSystem.IsWindows() &&
            Win32ConsoleApi.TryReadKey(_inputHandle, intercept, out var keyInfo))
            return keyInfo;

        return global::System.Console.ReadKey(intercept);
    }

    public void WriteAt(int x, int y, ReadOnlySpan<char> text,
        ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return;

        int width = global::System.Console.WindowWidth;
        int height = global::System.Console.WindowHeight;

        if (y >= height || x >= width)
            return;

        int maxLen = width - x;
        var span = text.Length > maxLen ? text[..maxLen] : text;
        var fg = foreground ?? global::System.Console.ForegroundColor;
        var bg = background ?? global::System.Console.BackgroundColor;

        if (OperatingSystem.IsWindows())
        {
            WriteAtWindows(x, y, span, fg, bg);
            return;
        }

        var prevFg = global::System.Console.ForegroundColor;
        var prevBg = global::System.Console.BackgroundColor;

        try
        {
            global::System.Console.ForegroundColor = fg;
            global::System.Console.BackgroundColor = bg;

            global::System.Console.SetCursorPosition(
                global::System.Console.WindowLeft + x,
                global::System.Console.WindowTop + y);
            global::System.Console.Write(span);
        }
        finally
        {
            global::System.Console.ForegroundColor = prevFg;
            global::System.Console.BackgroundColor = prevBg;
        }
    }

    public void ClearRegion(Rect region)
    {
        var size = GetSize();
        int y1 = Math.Max(0, region.Y);
        int y2 = Math.Min(size.Height, region.Bottom);
        int x1 = Math.Max(0, region.X);
        int x2 = Math.Min(size.Width, region.Right);
        int w = x2 - x1;

        if (w <= 0 || y2 <= y1)
            return;

        var spaces = new string(' ', w);
        for (int y = y1; y < y2; y++)
            WriteAt(x1, y, spaces.AsSpan());
    }

    public void SetCursorPosition(int x, int y)
    {
        if (!IsVisibleCursorPosition(x, y))
            return;

        try
        {
            global::System.Console.SetCursorPosition(
                global::System.Console.WindowLeft + x,
                global::System.Console.WindowTop + y);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Window size changed between validation and SetCursorPosition.
        }
    }

    public void SetCursorVisible(bool visible) =>
        global::System.Console.CursorVisible = visible;

    public ScreenSnapshot Capture(Rect region)
    {
        if (OperatingSystem.IsWindows())
            return CaptureWindows(region);
        return CaptureFallback(region);
    }

    public void Restore(ScreenSnapshot snapshot)
    {
        if (OperatingSystem.IsWindows())
            RestoreWindows(snapshot);
        else
            RestoreFallback(snapshot);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryConfigureInputMode(IntPtr inputHandle, out uint originalMode)
    {
        originalMode = 0;
        if (!Win32ConsoleApi.TryGetConsoleMode(inputHandle, out var mode))
            return false;

        originalMode = mode;
        uint appMode = mode;
        appMode |= Win32ConsoleApi.ENABLE_EXTENDED_FLAGS;
        appMode |= Win32ConsoleApi.ENABLE_MOUSE_INPUT;
        appMode &= ~Win32ConsoleApi.ENABLE_QUICK_EDIT_MODE;
        appMode &= ~Win32ConsoleApi.ENABLE_INSERT_MODE;
        appMode &= ~Win32ConsoleApi.ENABLE_VIRTUAL_TERMINAL_INPUT;

        return appMode == mode || Win32ConsoleApi.TrySetConsoleMode(inputHandle, appMode);
    }

    private bool HasVisibleWindowSizeChanged()
    {
        var size = GetSize();
        if (size.Width == _lastInputSize.Width &&
            size.Height == _lastInputSize.Height)
        {
            return false;
        }

        _lastInputSize = size;
        return true;
    }

    private static bool IsVisibleCursorPosition(int x, int y) =>
        x >= 0 &&
        y >= 0 &&
        x < global::System.Console.WindowWidth &&
        y < global::System.Console.WindowHeight;

    [SupportedOSPlatform("windows")]
    private static void WriteAtWindows(int x, int y, ReadOnlySpan<char> text, ConsoleColor foreground, ConsoleColor background)
    {
        int w = text.Length;
        var raw = new CharInfo[w];
        short attributes = Win32ConsoleApi.MakeAttributes(foreground, background);

        for (int col = 0; col < w; col++)
        {
            raw[col] = new CharInfo
            {
                UnicodeChar = text[col],
                Attributes = attributes,
            };
        }

        var sr = new SmallRect
        {
            Left = (short)(global::System.Console.WindowLeft + x),
            Top = (short)(global::System.Console.WindowTop + y),
            Right = (short)(global::System.Console.WindowLeft + x + w - 1),
            Bottom = (short)(global::System.Console.WindowTop + y),
        };

        Win32ConsoleApi.WriteRegion(Win32ConsoleApi.GetConsoleOutputHandle(), raw, sr);
    }

    [SupportedOSPlatform("windows")]
    private ScreenSnapshot CaptureWindows(Rect region)
    {
        var sr = new SmallRect
        {
            Left = (short)(global::System.Console.WindowLeft + region.X),
            Top = (short)(global::System.Console.WindowTop + region.Y),
            Right = (short)(global::System.Console.WindowLeft + region.Right - 1),
            Bottom = (short)(global::System.Console.WindowTop + region.Bottom - 1),
        };

        var raw = Win32ConsoleApi.ReadRegion(_consoleHandle, sr);
        var cells = new SnapshotCell[region.Height, region.Width];

        if (raw != null)
        {
            for (int row = 0; row < region.Height; row++)
            {
                for (int col = 0; col < region.Width; col++)
                {
                    var ci = raw[row * region.Width + col];
                    var (fg, bg) = Win32ConsoleApi.SplitAttributes(ci.Attributes);
                    cells[row, col] = new SnapshotCell
                    {
                        Character = ci.UnicodeChar == '\0' ? ' ' : ci.UnicodeChar,
                        Foreground = fg,
                        Background = bg,
                    };
                }
            }
        }

        return new ScreenSnapshot(region, cells);
    }

    private static ScreenSnapshot CaptureFallback(Rect region)
    {
        var cells = new SnapshotCell[region.Height, region.Width];
        for (int r = 0; r < region.Height; r++)
            for (int c = 0; c < region.Width; c++)
                cells[r, c] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };
        return new ScreenSnapshot(region, cells);
    }

    [SupportedOSPlatform("windows")]
    private void RestoreWindows(ScreenSnapshot snapshot)
    {
        int w = snapshot.Region.Width;
        int h = snapshot.Region.Height;
        var raw = new CharInfo[h * w];

        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                var cell = snapshot.Cells[row, col];
                raw[row * w + col] = new CharInfo
                {
                    UnicodeChar = cell.Character,
                    Attributes = Win32ConsoleApi.MakeAttributes(cell.Foreground, cell.Background),
                };
            }
        }

        var sr = new SmallRect
        {
            Left = (short)(global::System.Console.WindowLeft + snapshot.Region.X),
            Top = (short)(global::System.Console.WindowTop + snapshot.Region.Y),
            Right = (short)(global::System.Console.WindowLeft + snapshot.Region.Right - 1),
            Bottom = (short)(global::System.Console.WindowTop + snapshot.Region.Bottom - 1),
        };

        Win32ConsoleApi.WriteRegion(_consoleHandle, raw, sr);
    }

    private void RestoreFallback(ScreenSnapshot snapshot)
    {
        Span<char> ch = stackalloc char[1];
        for (int row = 0; row < snapshot.Region.Height; row++)
        {
            for (int col = 0; col < snapshot.Region.Width; col++)
            {
                var cell = snapshot.Cells[row, col];
                ch[0] = cell.Character;
                WriteAt(snapshot.Region.X + col, snapshot.Region.Y + row,
                    ch, cell.Foreground, cell.Background);
            }
        }
    }
}
