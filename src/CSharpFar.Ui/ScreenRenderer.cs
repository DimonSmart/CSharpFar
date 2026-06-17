using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console;

/// <summary>
/// Higher-level rendering surface built on top of <see cref="IConsoleDriver"/>.
/// Provides convenience methods for drawing text, boxes, and regions.
/// </summary>
public sealed class ScreenRenderer
{
    private readonly IConsoleDriver _driver;
    private SnapshotCell[,]? _frontBuffer;
    private SnapshotCell[,]? _backBuffer;
    private ConsoleSize _bufferSize;
    private bool _frontBufferKnown;
    private ConsoleViewport? _frontBufferViewport;
    private bool _frameActive;
    private ConsoleSize _frameSize;
    private ConsoleViewport _frameViewport;
    private bool _forceFullFrame;
    private bool? _cursorVisible;
    private int _pendingCursorX;
    private int _pendingCursorY;
    private bool _hasPendingCursorPosition;
    private bool? _pendingCursorVisible;
    private readonly Queue<ConsoleInputEvent> _pendingInputEvents = new();

    /// <summary>
    /// True if the last frame's flush was aborted mid-way because the console
    /// size changed during rendering. The caller should discard and re-render.
    /// </summary>
    public bool FrameWasInterrupted { get; private set; }

    public ScreenRenderer(IConsoleDriver driver)
    {
        _driver = driver;
    }

    public ConsoleViewport GetViewport() => _driver.GetViewport();

    public ConsoleSize GetSize() => _driver.GetSize();

    public bool TryScrollViewportToBottom()
    {
        if (_frameActive)
            throw new InvalidOperationException("Cannot scroll the viewport during an active render frame.");

        var before = _driver.GetViewport();
        if (!_driver.TryScrollViewportToBottom())
            return false;

        var after = _driver.GetViewport();
        if (after == before)
            return false;

        _frontBufferKnown = false;
        _frontBufferViewport = null;
        _forceFullFrame = true;
        return true;
    }

    /// <summary>
    /// The size captured at <see cref="BeginFrame"/>. All rendering within a frame
    /// must use this value — not a second call to <see cref="GetSize"/> — to guarantee
    /// that layout and clip bounds are consistent with the back-buffer dimensions.
    /// Only valid while a frame is active.
    /// </summary>
    public ConsoleSize FrameSize => _frameSize;

    /// <summary>
    /// The viewport captured at <see cref="BeginFrame"/>. Only valid while a frame is active.
    /// </summary>
    public ConsoleViewport FrameViewport => _frameViewport;

    public void SetRenderingOutputMode(bool enabled)
    {
        if (_driver is IConsoleOutputModeDriver outputModeDriver)
            outputModeDriver.SetRenderingOutputMode(enabled);
    }

    public void SetConsoleScrollbackEnabled(bool enabled)
    {
        if (_driver is IConsoleOutputModeDriver outputModeDriver)
            outputModeDriver.SetConsoleScrollbackEnabled(enabled);
    }

    public void RestoreApplicationInputMode()
    {
        if (_driver is IConsoleOutputModeDriver outputModeDriver)
            outputModeDriver.RestoreApplicationInputMode();
    }

    public IDisposable EnterChildProcessConsoleMode()
    {
        return _driver is IConsoleOutputModeDriver outputModeDriver
            ? outputModeDriver.EnterChildProcessConsoleMode()
            : EmptyDisposable.Instance;
    }

    public IDisposable BeginFrame()
    {
        if (_frameActive)
            throw new InvalidOperationException("A render frame is already active.");

        var viewport = _driver.GetViewport();
        var size = viewport.Size;
        EnsureBuffers(size);
        if (_frontBufferKnown &&
            (!_frontBufferViewport.HasValue || _frontBufferViewport.Value != viewport))
        {
            _frontBufferKnown = false;
            _forceFullFrame = true;
        }
        CopyFrontToBack();
        _hasPendingCursorPosition = false;
        _pendingCursorVisible = null;
        _frameSize = size;
        _frameViewport = viewport;
        _frameActive = true;
        FrameWasInterrupted = false;

        return new Frame(this);
    }

    public void Write(int x, int y, string text, CellStyle style) =>
        Write(x, y, text.AsSpan(), style);

    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return;

        var size = _frameActive ? _frameSize : _driver.GetSize();
        if (y >= size.Height || x >= size.Width)
            return;

        int len = Math.Min(text.Length, size.Width - x);
        var clipped = text[..len];

        if (_frameActive)
        {
            EnsureBuffers(size);
            WriteToBuffer(_backBuffer!, x, y, clipped, style);
            return;
        }

        _driver.WriteAt(x, y, clipped, style.Foreground, style.Background, style.Attributes);
        EnsureBuffers(size);
        WriteToBuffer(_frontBuffer!, x, y, clipped, style);
    }

    public void WriteChar(int x, int y, char ch, CellStyle style) =>
        Write(x, y, stackalloc char[] { ch }, style);

    /// <summary>Fills a region with spaces using the given style.</summary>
    public void FillRegion(Rect region, CellStyle style)
    {
        var size = _frameActive ? _frameSize : _driver.GetSize();
        int y1 = Math.Max(0, region.Y);
        int y2 = Math.Min(size.Height, region.Bottom);
        int x1 = Math.Max(0, region.X);
        int x2 = Math.Min(size.Width, region.Right);
        int w = x2 - x1;

        if (w <= 0 || y2 <= y1)
            return;

        if (_frameActive)
        {
            EnsureBuffers(size);
            FillBuffer(_backBuffer!, x1, y1, w, y2 - y1, style);
            return;
        }

        var spaces = new string(' ', w);
        for (int y = y1; y < y2; y++)
            _driver.WriteAt(x1, y, spaces.AsSpan(), style.Foreground, style.Background, style.Attributes);

        EnsureBuffers(size);
        FillBuffer(_frontBuffer!, x1, y1, w, y2 - y1, style);
        if (x1 == 0 && y1 == 0 && w == size.Width && y2 - y1 == size.Height)
        {
            _frontBufferKnown = true;
            _frontBufferViewport = _driver.GetViewport();
            _forceFullFrame = false;
        }
    }

    public void ClearRegion(Rect region)
    {
        if (_frameActive)
        {
            FillRegion(region, CellStyle.Default);
            return;
        }

        var size = _driver.GetSize();
        _driver.ClearRegion(region);

        int y1 = Math.Max(0, region.Y);
        int y2 = Math.Min(size.Height, region.Bottom);
        int x1 = Math.Max(0, region.X);
        int x2 = Math.Min(size.Width, region.Right);
        int w = x2 - x1;

        if (w <= 0 || y2 <= y1)
            return;

        EnsureBuffers(size);
        FillBuffer(_frontBuffer!, x1, y1, w, y2 - y1, CellStyle.Default);
        if (x1 == 0 && y1 == 0 && w == size.Width && y2 - y1 == size.Height)
        {
            _frontBufferKnown = true;
            _frontBufferViewport = _driver.GetViewport();
            _forceFullFrame = false;
        }
    }

    public void ClearScreen()
    {
        var size = _driver.GetSize();
        _driver.ClearRegion(new Rect(0, 0, size.Width, size.Height));
        EnsureBuffers(size);
        FillBuffer(_frontBuffer!, 0, 0, size.Width, size.Height, CellStyle.Default);
        _frontBufferKnown = true;
        _frontBufferViewport = _driver.GetViewport();
        _forceFullFrame = false;
        if (_frameActive && _backBuffer is not null)
            FillBuffer(_backBuffer, 0, 0, size.Width, size.Height, CellStyle.Default);
    }

    /// <summary>Draws a single-line box border.</summary>
    public void DrawBox(Rect rect, CellStyle style)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return;

        int x = rect.X;
        int y = rect.Y;
        int w = rect.Width;
        int h = rect.Height;

        // Corners
        WriteChar(x, y, '┌', style);
        WriteChar(x + w - 1, y, '┐', style);
        WriteChar(x, y + h - 1, '└', style);
        WriteChar(x + w - 1, y + h - 1, '┘', style);

        // Horizontal lines
        var hLine = new string('─', w - 2);
        Write(x + 1, y, hLine, style);
        Write(x + 1, y + h - 1, hLine, style);

        // Vertical lines
        for (int row = y + 1; row < y + h - 1; row++)
        {
            WriteChar(x, row, '│', style);
            WriteChar(x + w - 1, row, '│', style);
        }
    }

    /// <summary>Draws a double-line box border.</summary>
    public void DrawDoubleBox(Rect rect, CellStyle style)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return;

        int x = rect.X;
        int y = rect.Y;
        int w = rect.Width;
        int h = rect.Height;

        // Corners
        WriteChar(x, y, '╔', style);
        WriteChar(x + w - 1, y, '╗', style);
        WriteChar(x, y + h - 1, '╚', style);
        WriteChar(x + w - 1, y + h - 1, '╝', style);

        // Horizontal lines
        var hLine = new string('═', w - 2);
        Write(x + 1, y, hLine, style);
        Write(x + 1, y + h - 1, hLine, style);

        // Vertical lines
        for (int row = y + 1; row < y + h - 1; row++)
        {
            WriteChar(x, row, '║', style);
            WriteChar(x + w - 1, row, '║', style);
        }
    }

    public void SetCursorPosition(int x, int y)
    {
        if (_frameActive)
        {
            _pendingCursorX = x;
            _pendingCursorY = y;
            _hasPendingCursorPosition = true;
            return;
        }

        _driver.SetCursorPosition(x, y);
    }

    public void SetCursorVisible(bool visible)
    {
        if (_frameActive && !visible)
        {
            _pendingCursorVisible = false;
            ApplyCursorVisible(false);
            return;
        }

        if (_frameActive && visible)
        {
            _pendingCursorVisible = visible;
            return;
        }

        ApplyCursorVisible(visible);
    }

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default)
    {
        if (_pendingInputEvents.TryDequeue(out var pending))
            return pending;
        return _driver.ReadInput(true, cancellationToken);
    }

    public bool TryReadInput([NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        if (_pendingInputEvents.TryDequeue(out inputEvent))
            return true;
        return _driver.TryReadInput(true, out inputEvent);
    }

    /// <summary>
    /// Drains all pending resize events from the input queue, re-queuing any
    /// non-resize events so they are processed normally on the next iteration.
    /// Call this after waiting for the console to stabilise to avoid re-rendering
    /// stale resize events that accumulated while the window was being resized.
    /// </summary>
    public void DrainResizeEvents()
    {
        while (_driver.TryReadInput(true, out var evt))
        {
            if (evt is not ConsoleResizeInputEvent)
                _pendingInputEvents.Enqueue(evt);
        }
    }

    public ConsoleKeyInfo ReadKey()
    {
        while (_pendingInputEvents.TryDequeue(out var pending))
        {
            if (pending is KeyConsoleInputEvent keyEvent)
                return keyEvent.Key;

            if (pending is ConsoleResizeInputEvent)
                return new ConsoleKeyInfo('\0', ConsoleKey.NoName, shift: false, alt: false, control: false);
        }

        return _driver.ReadKey(true);
    }

    public ScreenSnapshot Capture(Rect region)
    {
        var snapshot = _driver.Capture(region);
        SyncFrontBuffer(snapshot, snapshot.Viewport);
        return snapshot;
    }

    public void Restore(ScreenSnapshot snapshot)
    {
        _driver.Restore(snapshot);
        SyncFrontBuffer(snapshot, _driver.GetViewport());
        if (_frameActive && _backBuffer is not null)
            CopySnapshotToBuffer(_backBuffer, snapshot);
    }

    private void EndFrame()
    {
        if (!_frameActive)
            return;

        FlushFrame();

        if (!FrameWasInterrupted &&
            (_hasPendingCursorPosition || _pendingCursorVisible.HasValue))
        {
            if (_hasPendingCursorPosition)
            {
                if (!_driver.TrySetCursorPositionInViewport(_frameViewport, _pendingCursorX, _pendingCursorY))
                    InterruptFrame();
            }
            if (!FrameWasInterrupted && _pendingCursorVisible.HasValue)
                ApplyCursorVisible(_pendingCursorVisible.Value);
        }

        _frameActive = false;
    }

    private void FlushFrame()
    {
        if (_frontBuffer is null || _backBuffer is null)
            return;

        bool forceFull = !_frontBufferKnown || _forceFullFrame;
        int height = _bufferSize.Height;
        int width  = _bufferSize.Width;

        for (int y = 0; y < height; y++)
        {
            var currentViewport = _driver.GetViewport();
            if (currentViewport != _frameViewport)
            {
                InterruptFrame();
                return;
            }

            int x = 0;
            while (x < width)
            {
                if (!IsDirty(y, x, forceFull))
                {
                    x++;
                    continue;
                }

                int start = x;
                var first = _backBuffer[y, x];
                x++;

                while (x < width &&
                       IsDirty(y, x, forceFull) &&
                       SameStyle(first, _backBuffer[y, x]))
                {
                    x++;
                }

                int len = x - start;
                var chars = new char[len];
                for (int i = 0; i < len; i++)
                    chars[i] = _backBuffer[y, start + i].Character;

                if (!_driver.TryWriteAtViewport(_frameViewport, start, y, chars, first.Foreground, first.Background, first.Attributes))
                {
                    InterruptFrame();
                    return;
                }
            }
        }

        if (_driver.GetViewport() != _frameViewport)
        {
            InterruptFrame();
            return;
        }

        Array.Copy(_backBuffer, _frontBuffer, _backBuffer.Length);
        _frontBufferKnown = true;
        _frontBufferViewport = _frameViewport;
        _forceFullFrame = false;
    }

    private void InterruptFrame()
    {
        FrameWasInterrupted = true;
        _frontBufferKnown = false;
        _frontBufferViewport = null;
        _forceFullFrame = true;
    }

    private bool IsDirty(int y, int x, bool forceFull) =>
        forceFull || !SameCell(_frontBuffer![y, x], _backBuffer![y, x]);

    private void EnsureBuffers(ConsoleSize size)
    {
        if (_frontBuffer is not null &&
            _backBuffer is not null &&
            _bufferSize.Width == size.Width &&
            _bufferSize.Height == size.Height)
        {
            return;
        }

        _bufferSize = size;
        _frontBuffer = CreateBuffer(size);
        _backBuffer = CreateBuffer(size);
        _frontBufferKnown = false;
        _frontBufferViewport = null;
        _forceFullFrame = true;
    }

    private void CopyFrontToBack()
    {
        if (_frontBuffer is null || _backBuffer is null)
            return;

        if (_frontBufferKnown)
            Array.Copy(_frontBuffer, _backBuffer, _frontBuffer.Length);
        else
            FillBuffer(_backBuffer, 0, 0, _bufferSize.Width, _bufferSize.Height, CellStyle.Default);
    }

    private static SnapshotCell[,] CreateBuffer(ConsoleSize size)
    {
        var buffer = new SnapshotCell[size.Height, size.Width];
        FillBuffer(buffer, 0, 0, size.Width, size.Height, CellStyle.Default);
        return buffer;
    }

    private static void WriteToBuffer(
        SnapshotCell[,] buffer,
        int x,
        int y,
        ReadOnlySpan<char> text,
        CellStyle style)
    {
        for (int i = 0; i < text.Length; i++)
        {
            buffer[y, x + i] = new SnapshotCell
            {
                Character = text[i],
                Foreground = style.Foreground,
                Background = style.Background,
                Attributes = style.Attributes,
            };
        }
    }

    private static void FillBuffer(SnapshotCell[,] buffer, int x, int y, int width, int height, CellStyle style)
    {
        var cell = new SnapshotCell
        {
            Character = ' ',
            Foreground = style.Foreground,
            Background = style.Background,
            Attributes = style.Attributes,
        };

        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++)
                buffer[row, col] = cell;
    }

    private void SyncFrontBuffer(ScreenSnapshot snapshot, ConsoleViewport viewport)
    {
        EnsureBuffers(_driver.GetSize());
        CopySnapshotToBuffer(_frontBuffer!, snapshot);

        if (snapshot.Region.X == 0 &&
            snapshot.Region.Y == 0 &&
            snapshot.Region.Width == _bufferSize.Width &&
            snapshot.Region.Height == _bufferSize.Height)
        {
            _frontBufferKnown = true;
            _frontBufferViewport = viewport;
            _forceFullFrame = false;
        }
    }

    private void CopySnapshotToBuffer(SnapshotCell[,] buffer, ScreenSnapshot snapshot)
    {
        int rowStart = Math.Max(0, -snapshot.Region.Y);
        int colStart = Math.Max(0, -snapshot.Region.X);
        int rowEnd = Math.Min(snapshot.Region.Height, _bufferSize.Height - snapshot.Region.Y);
        int colEnd = Math.Min(snapshot.Region.Width, _bufferSize.Width - snapshot.Region.X);

        for (int row = rowStart; row < rowEnd; row++)
        {
            for (int col = colStart; col < colEnd; col++)
            {
                int y = snapshot.Region.Y + row;
                int x = snapshot.Region.X + col;
                buffer[y, x] = snapshot.Cells[row, col];
            }
        }
    }

    private void ApplyCursorVisible(bool visible)
    {
        if (_cursorVisible == visible)
            return;

        _driver.SetCursorVisible(visible);
        _cursorVisible = visible;
    }

    private static bool SameCell(SnapshotCell left, SnapshotCell right) =>
        left.Character == right.Character &&
        SameStyle(left, right);

    private static bool SameStyle(SnapshotCell left, SnapshotCell right) =>
        left.Foreground == right.Foreground &&
        left.Background == right.Background &&
        left.Attributes == right.Attributes;

    private sealed class Frame : IDisposable
    {
        private ScreenRenderer? _owner;

        public Frame(ScreenRenderer owner) => _owner = owner;

        public void Dispose()
        {
            _owner?.EndFrame();
            _owner = null;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
