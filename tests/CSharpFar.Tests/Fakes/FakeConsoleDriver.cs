using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests.Fakes;

/// <summary>
/// In-memory console driver for unit tests.
/// Maintains a character/color buffer that can be inspected after rendering.
/// </summary>
public sealed class FakeConsoleDriver : IConsoleDriver
{
    private SnapshotCell[,] _buffer;
    private ConsoleSize _size;
    private readonly Queue<ConsoleKeyInfo> _keyQueue = new();

    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;

    public FakeConsoleDriver(int width = 80, int height = 25)
    {
        _size = new ConsoleSize(width, height);
        _buffer = CreateBuffer(width, height);
    }

    private static SnapshotCell[,] CreateBuffer(int width, int height)
    {
        var buf = new SnapshotCell[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                buf[y, x] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };
        return buf;
    }

    public ConsoleSize GetSize() => _size;

    public void EnqueueKey(ConsoleKeyInfo key) => _keyQueue.Enqueue(key);

    public ConsoleKeyInfo ReadKey(bool intercept) =>
        _keyQueue.TryDequeue(out var key)
            ? key
            : throw new InvalidOperationException("No keys queued in FakeConsoleDriver.");

    public void WriteAt(int x, int y, ReadOnlySpan<char> text,
        ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        if (text.IsEmpty || x < 0 || y < 0 || y >= _size.Height)
            return;

        var fg = foreground ?? ConsoleColor.Gray;
        var bg = background ?? ConsoleColor.Black;

        for (int i = 0; i < text.Length; i++)
        {
            int col = x + i;
            if (col >= _size.Width) break;
            _buffer[y, col] = new SnapshotCell { Character = text[i], Foreground = fg, Background = bg };
        }
    }

    public void ClearRegion(Rect region)
    {
        for (int y = Math.Max(0, region.Y); y < Math.Min(_size.Height, region.Bottom); y++)
            for (int x = Math.Max(0, region.X); x < Math.Min(_size.Width, region.Right); x++)
                _buffer[y, x] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };
    }

    public void SetCursorPosition(int x, int y) { CursorX = x; CursorY = y; }
    public void SetCursorVisible(bool visible) { CursorVisible = visible; }

    public ScreenSnapshot Capture(Rect region)
    {
        var cells = new SnapshotCell[region.Height, region.Width];
        for (int row = 0; row < region.Height; row++)
            for (int col = 0; col < region.Width; col++)
            {
                int ry = region.Y + row;
                int rx = region.X + col;
                if (ry >= 0 && ry < _size.Height && rx >= 0 && rx < _size.Width)
                    cells[row, col] = _buffer[ry, rx];
            }
        return new ScreenSnapshot(region, cells);
    }

    public void Restore(ScreenSnapshot snapshot)
    {
        for (int row = 0; row < snapshot.Region.Height; row++)
            for (int col = 0; col < snapshot.Region.Width; col++)
            {
                int dy = snapshot.Region.Y + row;
                int dx = snapshot.Region.X + col;
                if (dy >= 0 && dy < _size.Height && dx >= 0 && dx < _size.Width)
                    _buffer[dy, dx] = snapshot.Cells[row, col];
            }
    }

    // --- Inspection helpers ---

    public SnapshotCell GetCell(int x, int y) => _buffer[y, x];

    public string GetRow(int y) =>
        new(Enumerable.Range(0, _size.Width).Select(x => _buffer[y, x].Character).ToArray());

    public string GetRegionText(Rect region)
    {
        var sb = new System.Text.StringBuilder();
        for (int row = region.Y; row < region.Bottom; row++)
        {
            for (int col = region.X; col < region.Right; col++)
                sb.Append(_buffer[row, col].Character);
            if (row < region.Bottom - 1)
                sb.AppendLine();
        }
        return sb.ToString();
    }
}
