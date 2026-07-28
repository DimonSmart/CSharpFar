using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.DemoRecorder;

internal sealed class RecordingConsoleDriver : IConsoleDriver, IConsoleOutputModeDriver, ITerminalScreenMode, IDisposable
{
    private SnapshotCell[,] _buffer;
    private readonly Queue<ConsoleInputEvent> _inputQueue = new();
    private ConsoleSize _size;

    public RecordingConsoleDriver(int width, int height)
    {
        _size = new ConsoleSize(width, height);
        _buffer = CreateBuffer(width, height);
    }

    public Action<RecordingConsoleDriver>? BeforeReadInput { get; set; }
    public bool IsSupported { get; set; }
    public bool IsApplicationScreenActive { get; private set; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;

    public ConsoleViewport GetViewport() => new(0, 0, _size.Width, _size.Height);
    public ConsoleSize GetSize() => _size;
    public bool TryScrollViewportToBottom() => false;
    public bool TryIsViewportAtBottom(out bool isAtBottom)
    {
        isAtBottom = true;
        return true;
    }

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeReadInput?.Invoke(this);
        if (_inputQueue.TryDequeue(out ConsoleInputEvent? input))
            return input;

        throw new InvalidOperationException("No scripted input was available for the recording run.");
    }

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        BeforeReadInput?.Invoke(this);
        return _inputQueue.TryDequeue(out inputEvent);
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        ConsoleInputEvent input = ReadInput(intercept);
        return input is KeyConsoleInputEvent keyEvent
            ? keyEvent.Key
            : throw new InvalidOperationException("The next scripted input was not a keyboard event.");
    }

    public void WriteAt(
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null,
        TextAttributes attributes = TextAttributes.None)
    {
        if (x < 0 || y < 0 || y >= _size.Height || text.IsEmpty)
            return;

        var fg = foreground ?? ConsoleColor.Gray;
        var bg = background ?? ConsoleColor.Black;
        string rendered = text.ToString();
        for (int i = 0; i < rendered.Length && x + i < _size.Width; i++)
        {
            _buffer[y, x + i] = new SnapshotCell
            {
                Character = rendered[i],
                Foreground = fg,
                Background = bg,
                Attributes = attributes,
            };
        }
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
        if (viewport != GetViewport())
            return false;

        WriteAt(x, y, text, foreground, background, attributes);
        return true;
    }

    public void ClearRegion(Rect region)
    {
        int top = Math.Max(0, region.Y);
        int left = Math.Max(0, region.X);
        int bottom = Math.Min(_size.Height, region.Bottom);
        int right = Math.Min(_size.Width, region.Right);
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                _buffer[y, x] = CreateDefaultCell();
    }

    public void SetCursorPosition(int x, int y)
    {
        CursorX = x;
        CursorY = y;
    }

    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        if (viewport != GetViewport() || !viewport.ContainsRelative(x, y))
            return false;

        CursorX = x;
        CursorY = y;
        return true;
    }

    public void SetCursorVisible(bool visible) => CursorVisible = visible;
    public void SetRenderingOutputMode(bool enabled) { }
    public void SetConsoleScrollbackEnabled(bool enabled) { }
    public void RestoreApplicationInputMode() { }
    public IDisposable EnterChildProcessConsoleMode() => NoOpDisposable.Instance;

    public ScreenSnapshot Capture(Rect region)
    {
        var cells = new SnapshotCell[region.Height, region.Width];
        for (int row = 0; row < region.Height; row++)
        {
            for (int column = 0; column < region.Width; column++)
            {
                int sourceY = region.Y + row;
                int sourceX = region.X + column;
                if (sourceY >= 0 && sourceY < _size.Height && sourceX >= 0 && sourceX < _size.Width)
                    cells[row, column] = _buffer[sourceY, sourceX];
                else
                    cells[row, column] = CreateDefaultCell();
            }
        }

        return new ScreenSnapshot(GetViewport(), region, cells);
    }

    public void Restore(ScreenSnapshot snapshot)
    {
        for (int row = 0; row < snapshot.Region.Height; row++)
        {
            for (int column = 0; column < snapshot.Region.Width; column++)
            {
                int targetY = snapshot.Region.Y + row;
                int targetX = snapshot.Region.X + column;
                if (targetY >= 0 && targetY < _size.Height && targetX >= 0 && targetX < _size.Width)
                    _buffer[targetY, targetX] = snapshot.Cells[row, column];
            }
        }
    }

    public void EnterApplicationScreen()
    {
        if (IsSupported)
            IsApplicationScreenActive = true;
    }

    public void LeaveApplicationScreen()
    {
        if (IsSupported)
            IsApplicationScreenActive = false;
    }

    public void EnsureApplicationScreen() => EnterApplicationScreen();
    public void EnsureMainScreen() => LeaveApplicationScreen();
    public void RestoreTerminal() => IsApplicationScreenActive = false;

    public void EnqueueInput(ConsoleInputEvent inputEvent) => _inputQueue.Enqueue(inputEvent);
    public ScreenSnapshot CaptureViewport() => Capture(new Rect(0, 0, _size.Width, _size.Height));
    public void Dispose() { }

    private static SnapshotCell[,] CreateBuffer(int width, int height)
    {
        var buffer = new SnapshotCell[height, width];
        SnapshotCell cell = CreateDefaultCell();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                buffer[y, x] = cell;
        return buffer;
    }

    private static SnapshotCell CreateDefaultCell() =>
        new()
        {
            Character = ' ',
            Foreground = ConsoleColor.Gray,
            Background = ConsoleColor.Black,
            Attributes = TextAttributes.None,
        };

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
