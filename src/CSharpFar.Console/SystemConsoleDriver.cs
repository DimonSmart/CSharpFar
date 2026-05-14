using System.Diagnostics.CodeAnalysis;
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
    private readonly Win32ModifierKeyTracker? _modifierKeyTracker;
    private ConsoleViewport _lastInputViewport;
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
            _modifierKeyTracker = new Win32ModifierKeyTracker();
        }

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        _lastInputViewport = GetViewport();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (OperatingSystem.IsWindows() && _restoreInputMode)
            Win32ConsoleApi.TrySetConsoleMode(_inputHandle, _originalInputMode);
        if (OperatingSystem.IsWindows() && _restoreOutputMode)
            Win32ConsoleApi.TrySetConsoleMode(_consoleHandle, _originalOutputMode);
        if (OperatingSystem.IsWindows())
            _modifierKeyTracker?.Dispose();

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

    public ConsoleViewport GetViewport()
    {
        if (OperatingSystem.IsWindows() &&
            Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var sbi))
        {
            int w = sbi.srWindow.Right  - sbi.srWindow.Left + 1;
            int h = sbi.srWindow.Bottom - sbi.srWindow.Top  + 1;
            return new ConsoleViewport(sbi.srWindow.Left, sbi.srWindow.Top, w, h);
        }

        return new ConsoleViewport(
            global::System.Console.WindowLeft,
            global::System.Console.WindowTop,
            global::System.Console.WindowWidth,
            global::System.Console.WindowHeight);
    }

    public ConsoleSize GetSize() => GetViewport().Size;

    public bool TryScrollViewportToBottom()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var sbi))
            return false;

        var current = ToViewport(sbi);
        if (current.Width <= 0 || current.Height <= 0 || sbi.dwSize.X <= 0 || sbi.dwSize.Y <= 0)
            return false;

        int targetBottom = sbi.dwSize.Y - 1;
        int targetTop = Math.Max(0, targetBottom - current.Height + 1);
        int targetLeft = Math.Clamp(current.Left, 0, Math.Max(0, sbi.dwSize.X - current.Width));
        int targetRight = targetLeft + current.Width - 1;

        var target = new SmallRect
        {
            Left = (short)targetLeft,
            Top = (short)targetTop,
            Right = (short)targetRight,
            Bottom = (short)targetBottom,
        };

        if (sbi.srWindow.Left == target.Left &&
            sbi.srWindow.Top == target.Top &&
            sbi.srWindow.Right == target.Right &&
            sbi.srWindow.Bottom == target.Bottom)
        {
            return false;
        }

        if (!Win32ConsoleApi.TrySetConsoleWindowInfo(_consoleHandle, target))
            return false;

        _lastInputViewport = GetViewport();
        return _lastInputViewport != current;
    }

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            var inputEvent = Win32ConsoleApi.ReadInput(
                _inputHandle,
                intercept,
                cancellationToken,
                _modifierKeyTracker,
                HasVisibleViewportChanged);

            if (inputEvent is ConsoleResizeInputEvent)
                _lastInputViewport = GetViewport();

            return inputEvent;
        }

        // Non-Windows fallback: key-only
        cancellationToken.ThrowIfCancellationRequested();
        var key = global::System.Console.ReadKey(intercept);
        return new KeyConsoleInputEvent(key);
    }

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        if (OperatingSystem.IsWindows())
        {
            bool hasInput = Win32ConsoleApi.TryReadInput(
                _inputHandle,
                intercept,
                out inputEvent,
                _modifierKeyTracker);

            if (hasInput && inputEvent is ConsoleResizeInputEvent)
                _lastInputViewport = GetViewport();
            return hasInput;
        }

        try
        {
            if (!global::System.Console.KeyAvailable)
            {
                inputEvent = null;
                return false;
            }

            inputEvent = new KeyConsoleInputEvent(global::System.Console.ReadKey(intercept));
            return true;
        }
        catch (InvalidOperationException)
        {
            inputEvent = null;
            return false;
        }
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

        if (OperatingSystem.IsWindows())
        {
            var fg2 = foreground ?? global::System.Console.ForegroundColor;
            var bg2 = background ?? global::System.Console.BackgroundColor;
            TryWriteAtViewport(GetViewport(), x, y, text, fg2, bg2);
            return;
        }

        int width  = global::System.Console.WindowWidth;
        int height = global::System.Console.WindowHeight;

        if (y >= height || x >= width)
            return;

        int maxLen = width - x;
        var span = text.Length > maxLen ? text[..maxLen] : text;
        var fg = foreground ?? global::System.Console.ForegroundColor;
        var bg = background ?? global::System.Console.BackgroundColor;
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

    public bool TryWriteAtViewport(
        ConsoleViewport viewport,
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return true;

        if (OperatingSystem.IsWindows())
        {
            var fg2 = foreground ?? global::System.Console.ForegroundColor;
            var bg2 = background ?? global::System.Console.BackgroundColor;
            return TryWriteAtWindows(viewport, x, y, text, fg2, bg2);
        }

        if (GetViewport() != viewport)
            return false;

        WriteAt(x, y, text, foreground, background);
        return true;
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
        if (OperatingSystem.IsWindows())
        {
            TrySetCursorPositionInViewport(GetViewport(), x, y);
            return;
        }

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

    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        if (x < 0 || y < 0 || x >= viewport.Width || y >= viewport.Height)
            return false;

        if (OperatingSystem.IsWindows())
        {
            if (!Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var before))
                return false;

            var current = ToViewport(before);
            if (current != viewport)
                return false;

            int absX = viewport.Left + x;
            int absY = viewport.Top  + y;
            if (!viewport.ContainsAbsolute(absX, absY))
                return false;

            if (!Win32ConsoleApi.TrySetConsoleCursorPositionDirect(_consoleHandle, (short)absX, (short)absY))
                return false;

            return Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var after) &&
                   ToViewport(after) == viewport;
        }

        if (GetViewport() != viewport)
            return false;

        try
        {
            global::System.Console.SetCursorPosition(viewport.Left + x, viewport.Top + y);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public void SetCursorVisible(bool visible) =>
        global::System.Console.CursorVisible = visible;

    public ScreenSnapshot Capture(Rect region)
    {
        if (OperatingSystem.IsWindows())
            return CaptureWindows(region);
        return CaptureFallback(GetViewport(), region);
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
        uint appMode = GetApplicationInputMode(mode);

        return appMode == mode || Win32ConsoleApi.TrySetConsoleMode(inputHandle, appMode);
    }

    [SupportedOSPlatform("windows")]
    internal static uint GetApplicationInputMode(uint mode)
    {
        uint appMode = mode;
        appMode |= Win32ConsoleApi.ENABLE_EXTENDED_FLAGS;
        appMode |= Win32ConsoleApi.ENABLE_MOUSE_INPUT;
        appMode |= Win32ConsoleApi.ENABLE_WINDOW_INPUT;
        appMode &= ~Win32ConsoleApi.ENABLE_PROCESSED_INPUT;
        appMode &= ~Win32ConsoleApi.ENABLE_LINE_INPUT;
        appMode &= ~Win32ConsoleApi.ENABLE_ECHO_INPUT;
        appMode &= ~Win32ConsoleApi.ENABLE_QUICK_EDIT_MODE;
        appMode &= ~Win32ConsoleApi.ENABLE_INSERT_MODE;
        appMode &= ~Win32ConsoleApi.ENABLE_VIRTUAL_TERMINAL_INPUT;
        return appMode;
    }

    private bool HasVisibleViewportChanged()
    {
        var viewport = GetViewport();
        if (viewport == _lastInputViewport)
            return false;

        _lastInputViewport = viewport;
        return true;
    }

    private static bool IsVisibleCursorPosition(int x, int y) =>
        x >= 0 &&
        y >= 0 &&
        x < global::System.Console.WindowWidth &&
        y < global::System.Console.WindowHeight;

    [SupportedOSPlatform("windows")]
    private bool TryWriteAtWindows(
        ConsoleViewport viewport,
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor foreground,
        ConsoleColor background)
    {
        if (!Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var sbi))
            return false;

        if (ToViewport(sbi) != viewport)
            return false;

        if (y >= viewport.Height || x >= viewport.Width)
            return true;

        int absLeft = viewport.Left + x;
        int absTop  = viewport.Top  + y;

        if (!viewport.ContainsAbsolute(absLeft, absTop))
            return false;

        int maxLen = viewport.Right - absLeft + 1;
        var span = text.Length > maxLen ? text[..maxLen] : text;
        int w = span.Length;
        if (w <= 0)
            return true;

        var raw = new CharInfo[w];
        short attributes = Win32ConsoleApi.MakeAttributes(foreground, background);
        for (int col = 0; col < w; col++)
            raw[col] = new CharInfo { UnicodeChar = span[col], Attributes = attributes };

        var sr = new SmallRect
        {
            Left   = (short)absLeft,
            Top    = (short)absTop,
            Right  = (short)(absLeft + w - 1),
            Bottom = (short)absTop,
        };

        Win32ConsoleApi.WriteRegion(_consoleHandle, raw, sr);
        return Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var after) &&
               ToViewport(after) == viewport;
    }

    [SupportedOSPlatform("windows")]
    private static ConsoleViewport ToViewport(Win32ConsoleApi.ConsoleScreenBufferInfo sbi)
    {
        int width = sbi.srWindow.Right - sbi.srWindow.Left + 1;
        int height = sbi.srWindow.Bottom - sbi.srWindow.Top + 1;
        return new ConsoleViewport(sbi.srWindow.Left, sbi.srWindow.Top, width, height);
    }

    [SupportedOSPlatform("windows")]
    private ScreenSnapshot CaptureWindows(Rect region)
    {
        if (!Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var sbi))
            return CaptureFallback(GetViewport(), region);

        var viewport = ToViewport(sbi);

        var sr = new SmallRect
        {
            Left   = (short)(viewport.Left + region.X),
            Top    = (short)(viewport.Top  + region.Y),
            Right  = (short)(viewport.Left + region.Right  - 1),
            Bottom = (short)(viewport.Top  + region.Bottom - 1),
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

        return new ScreenSnapshot(viewport, region, cells);
    }

    private static ScreenSnapshot CaptureFallback(ConsoleViewport viewport, Rect region)
    {
        var cells = new SnapshotCell[region.Height, region.Width];
        for (int r = 0; r < region.Height; r++)
            for (int c = 0; c < region.Width; c++)
                cells[r, c] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };
        return new ScreenSnapshot(viewport, region, cells);
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

        if (!Win32ConsoleApi.TryGetConsoleScreenBufferInfo(_consoleHandle, out var sbi))
            return;

        var sr = new SmallRect
        {
            Left   = (short)(sbi.srWindow.Left + snapshot.Region.X),
            Top    = (short)(sbi.srWindow.Top  + snapshot.Region.Y),
            Right  = (short)(sbi.srWindow.Left + snapshot.Region.Right  - 1),
            Bottom = (short)(sbi.srWindow.Top  + snapshot.Region.Bottom - 1),
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
