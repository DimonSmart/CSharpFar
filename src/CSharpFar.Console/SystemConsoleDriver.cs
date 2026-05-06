using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Console.Models;
using CSharpFar.Console.Win32;

namespace CSharpFar.Console;

/// <summary>
/// Real console driver backed by System.Console.
/// On Windows, Capture/Restore use Win32 ReadConsoleOutput/WriteConsoleOutput
/// so that shell output underneath the panels is preserved for Ctrl+O.
/// This class targets Windows. On other platforms, Capture/Restore use a blank fallback.
/// </summary>
public sealed class SystemConsoleDriver : IConsoleDriver
{
    private readonly IntPtr _consoleHandle;

    public SystemConsoleDriver()
    {
        if (OperatingSystem.IsWindows())
            _consoleHandle = Win32ConsoleApi.GetConsoleOutputHandle();

        global::System.Console.OutputEncoding = System.Text.Encoding.UTF8;
    }

    public ConsoleSize GetSize() =>
        new(global::System.Console.BufferWidth, global::System.Console.WindowHeight);

    public ConsoleKeyInfo ReadKey(bool intercept) =>
        global::System.Console.ReadKey(intercept);

    public void WriteAt(int x, int y, ReadOnlySpan<char> text,
        ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return;

        int width = global::System.Console.BufferWidth;
        int height = global::System.Console.WindowHeight;

        if (y >= height || x >= width)
            return;

        int maxLen = width - x;
        var span = text.Length > maxLen ? text[..maxLen] : text;

        var prevFg = global::System.Console.ForegroundColor;
        var prevBg = global::System.Console.BackgroundColor;

        try
        {
            if (foreground.HasValue) global::System.Console.ForegroundColor = foreground.Value;
            if (background.HasValue) global::System.Console.BackgroundColor = background.Value;

            global::System.Console.SetCursorPosition(x, y);
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
        var prevFg = global::System.Console.ForegroundColor;
        var prevBg = global::System.Console.BackgroundColor;

        try
        {
            for (int y = y1; y < y2; y++)
            {
                global::System.Console.SetCursorPosition(x1, y);
                global::System.Console.Write(spaces);
            }
        }
        finally
        {
            global::System.Console.ForegroundColor = prevFg;
            global::System.Console.BackgroundColor = prevBg;
        }
    }

    public void SetCursorPosition(int x, int y) =>
        global::System.Console.SetCursorPosition(x, y);

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
    private ScreenSnapshot CaptureWindows(Rect region)
    {
        var sr = new SmallRect
        {
            Left = (short)region.X,
            Top = (short)region.Y,
            Right = (short)(region.Right - 1),
            Bottom = (short)(region.Bottom - 1),
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
            Left = (short)snapshot.Region.X,
            Top = (short)snapshot.Region.Y,
            Right = (short)(snapshot.Region.Right - 1),
            Bottom = (short)(snapshot.Region.Bottom - 1),
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
