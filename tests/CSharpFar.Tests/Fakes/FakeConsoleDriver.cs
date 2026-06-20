using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests.Fakes;

/// <summary>
/// In-memory console driver for unit tests.
/// Maintains a character/color buffer that can be inspected after rendering.
/// </summary>
public sealed class FakeConsoleDriver : IConsoleDriver, IConsoleOutputModeDriver, ITerminalScreenMode
{
    public readonly record struct WriteRecord(
        int X,
        int Y,
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background,
        TextAttributes Attributes);

    private SnapshotCell[,] _buffer;
    private ConsoleSize _size;
    private int _bufferHeight;
    private int _scrollbackBufferHeight;
    private int _viewportLeft;
    private int _viewportTop;
    private readonly Queue<ConsoleInputEvent> _inputQueue = new();
    private readonly List<WriteRecord> _writeRecords = [];
    private readonly List<string> _operationLog = [];

    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public int WriteAtCallCount { get; private set; }
    public int ClearRegionCallCount { get; private set; }
    public int RestoreCallCount { get; private set; }
    public int SetCursorVisibleCallCount { get; private set; }
    public int TrySetCursorPositionInViewportCallCount { get; private set; }
    public int TryScrollViewportToBottomCallCount { get; private set; }
    public bool RenderingOutputMode { get; private set; }
    public bool ConsoleScrollbackEnabled { get; private set; } = true;
    public int SetConsoleScrollbackEnabledCallCount { get; private set; }
    public bool ChildProcessConsoleMode { get; private set; }
    public int EnterChildProcessConsoleModeCallCount { get; private set; }
    public int RestoreApplicationInputModeCallCount { get; private set; }
    public bool IsSupported { get; set; }
    public bool IsApplicationScreenActive { get; private set; }
    public int EnterApplicationScreenCallCount { get; private set; }
    public int LeaveApplicationScreenCallCount { get; private set; }
    public int RestoreTerminalCallCount { get; private set; }
    public Action<FakeConsoleDriver>? BeforeReadInput { get; set; }
    public Action<FakeConsoleDriver>? BeforeTryReadInput { get; set; }
    public IReadOnlyList<WriteRecord> WriteRecords => _writeRecords;
    public IReadOnlyList<string> OperationLog => _operationLog;
    public event Action<WriteRecord>? Wrote;

    public FakeConsoleDriver(int width = 80, int height = 25)
    {
        _size = new ConsoleSize(width, height);
        _bufferHeight = height;
        _scrollbackBufferHeight = height;
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

    public ConsoleViewport GetViewport() => new(_viewportLeft, _viewportTop, _size.Width, _size.Height);

    public ConsoleSize GetSize() => _size;

    public bool TryScrollViewportToBottom()
    {
        TryScrollViewportToBottomCallCount++;
        _operationLog.Add("TryScrollViewportToBottom");
        int targetTop = Math.Max(0, _bufferHeight - _size.Height);
        if (_viewportTop == targetTop)
            return false;

        _viewportTop = targetTop;
        return true;
    }

    public void SetViewportOrigin(int left, int top)
    {
        _viewportLeft = left;
        _viewportTop = top;
    }

    public void SetBufferHeight(int height)
    {
        if (height < _size.Height)
            throw new ArgumentOutOfRangeException(nameof(height), "Buffer height cannot be smaller than the viewport height.");

        _bufferHeight = height;
        _scrollbackBufferHeight = Math.Max(_scrollbackBufferHeight, height);
    }

    public void SetSize(int width, int height)
    {
        _size = new ConsoleSize(width, height);
        _bufferHeight = Math.Max(_bufferHeight, height);
        _buffer = CreateBuffer(width, height);
    }

    public void EnqueueKey(ConsoleKeyInfo key) =>
        _inputQueue.Enqueue(new KeyConsoleInputEvent(key));

    public void EnqueueInput(ConsoleInputEvent inputEvent) =>
        _inputQueue.Enqueue(inputEvent);

    public ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvokeBeforeReadInput();
        return _inputQueue.TryDequeue(out var inputEvent)
            ? inputEvent
            : throw new InvalidOperationException("No input events queued in FakeConsoleDriver.");
    }

    public bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        InvokeBeforeTryReadInput();
        return _inputQueue.TryDequeue(out inputEvent);
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (!_inputQueue.TryDequeue(out var inputEvent))
            throw new InvalidOperationException("No input events queued in FakeConsoleDriver.");

        return inputEvent is KeyConsoleInputEvent keyEvent
            ? keyEvent.Key
            : throw new InvalidOperationException("Next queued input event is not a key.");
    }

    public void WriteAt(
        int x,
        int y,
        ReadOnlySpan<char> text,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null,
        TextAttributes attributes = TextAttributes.None)
    {
        if (text.IsEmpty || x < 0 || y < 0 || y >= _size.Height)
            return;

        var fg = foreground ?? ConsoleColor.Gray;
        var bg = background ?? ConsoleColor.Black;
        WriteAtCallCount++;
        _operationLog.Add("WriteAt");
        var record = new WriteRecord(x, y, text.ToString(), fg, bg, attributes);
        _writeRecords.Add(record);
        Wrote?.Invoke(record);

        for (int i = 0; i < text.Length; i++)
        {
            int col = x + i;
            if (col >= _size.Width) break;
            _buffer[y, col] = new SnapshotCell { Character = text[i], Foreground = fg, Background = bg, Attributes = attributes };
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
        ClearRegionCallCount++;
        for (int y = Math.Max(0, region.Y); y < Math.Min(_size.Height, region.Bottom); y++)
            for (int x = Math.Max(0, region.X); x < Math.Min(_size.Width, region.Right); x++)
                _buffer[y, x] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };
    }

    public void SetCursorPosition(int x, int y) { CursorX = x; CursorY = y; }
    public bool TrySetCursorPositionInViewport(ConsoleViewport viewport, int x, int y)
    {
        TrySetCursorPositionInViewportCallCount++;
        if (viewport != GetViewport())
            return false;

        if (x < 0 || y < 0 || x >= viewport.Width || y >= viewport.Height)
            return false;

        CursorX = viewport.Left + x;
        CursorY = viewport.Top + y;
        return true;
    }
    public void SetCursorVisible(bool visible) { CursorVisible = visible; SetCursorVisibleCallCount++; }
    public void SetRenderingOutputMode(bool enabled) { RenderingOutputMode = enabled; }
    public void SetConsoleScrollbackEnabled(bool enabled)
    {
        SetConsoleScrollbackEnabledCallCount++;
        if (enabled)
        {
            ConsoleScrollbackEnabled = true;
            _bufferHeight = Math.Max(_bufferHeight, _scrollbackBufferHeight);
            TryScrollViewportToBottom();
            return;
        }

        ConsoleScrollbackEnabled = false;
        _viewportLeft = 0;
        _viewportTop = 0;
        _bufferHeight = _size.Height;
    }

    public void EnterApplicationScreen()
    {
        if (!IsSupported || IsApplicationScreenActive)
            return;

        IsApplicationScreenActive = true;
        EnterApplicationScreenCallCount++;
        _operationLog.Add("EnterApplicationScreen");
    }

    public void LeaveApplicationScreen()
    {
        if (!IsSupported || !IsApplicationScreenActive)
            return;

        IsApplicationScreenActive = false;
        LeaveApplicationScreenCallCount++;
        _operationLog.Add("LeaveApplicationScreen");
    }

    public void EnsureApplicationScreen() => EnterApplicationScreen();

    public void EnsureMainScreen() => LeaveApplicationScreen();

    public void RestoreTerminal()
    {
        if (!IsSupported)
            return;

        IsApplicationScreenActive = false;
        RestoreTerminalCallCount++;
    }
    public void RestoreApplicationInputMode()
    {
        RestoreApplicationInputModeCallCount++;
        ChildProcessConsoleMode = false;
    }

    public IDisposable EnterChildProcessConsoleMode()
    {
        EnterChildProcessConsoleModeCallCount++;
        ChildProcessConsoleMode = true;
        return new ChildProcessConsoleModeScope(this);
    }

    public ScreenSnapshot Capture(Rect region)
    {
        _operationLog.Add("Capture");
        var cells = new SnapshotCell[region.Height, region.Width];
        for (int row = 0; row < region.Height; row++)
            for (int col = 0; col < region.Width; col++)
            {
                int ry = region.Y + row;
                int rx = region.X + col;
                if (ry >= 0 && ry < _size.Height && rx >= 0 && rx < _size.Width)
                    cells[row, col] = _buffer[ry, rx];
            }
        return new ScreenSnapshot(GetViewport(), region, cells);
    }

    public void Restore(ScreenSnapshot snapshot)
    {
        RestoreCallCount++;
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

    public void ClearRecordedOperations()
    {
        WriteAtCallCount = 0;
        ClearRegionCallCount = 0;
        RestoreCallCount = 0;
        SetCursorVisibleCallCount = 0;
        TrySetCursorPositionInViewportCallCount = 0;
        TryScrollViewportToBottomCallCount = 0;
        SetConsoleScrollbackEnabledCallCount = 0;
        _writeRecords.Clear();
        _operationLog.Clear();
    }

    private void InvokeBeforeReadInput()
    {
        var callback = BeforeReadInput;
        if (callback is null)
            return;

        BeforeReadInput = null;
        callback(this);
    }

    private void InvokeBeforeTryReadInput()
    {
        var callback = BeforeTryReadInput;
        if (callback is null)
            return;

        BeforeTryReadInput = null;
        callback(this);
    }

    private sealed class ChildProcessConsoleModeScope : IDisposable
    {
        private readonly FakeConsoleDriver _owner;
        private bool _disposed;

        public ChildProcessConsoleModeScope(FakeConsoleDriver owner)
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
