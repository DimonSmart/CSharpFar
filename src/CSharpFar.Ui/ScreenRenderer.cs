using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Console;

/// <summary>
/// Higher-level rendering surface built on top of <see cref="IConsoleDriver"/>.
/// Provides convenience methods for drawing text, boxes, and regions.
/// </summary>
public sealed class ScreenRenderer
{
    private enum FrameOwnership
    {
        Application,
        CapturedExternalSurface,
    }

    private readonly IConsoleDriver _driver;
    private BufferCell[,]? _frontBuffer;
    private BufferCell[,]? _backBuffer;
    private ConsoleSize _bufferSize;
    private bool _frontBufferKnown;
    private ConsoleViewport? _frontBufferViewport;
    private bool _frameActive;
    private ConsoleSize _frameSize;
    private ConsoleViewport _frameViewport;
    private bool _forceFullFrame;
    private FrameOwnership _frameOwnership;
    private bool? _cursorVisible;
    private int _pendingCursorX;
    private int _pendingCursorY;
    private bool _hasPendingCursorPosition;
    private bool? _pendingCursorVisible;
    private readonly Queue<ConsoleInputEvent> _pendingInputEvents = new();
    private readonly ScreenPresentationMode _presentationMode;
    private ConsoleOutputCell[] _presentationBuffer = [];
    private int _presentationBufferCount;
    private readonly List<ConsoleOutputRun> _presentationRuns = [];

    internal FramePresentationMetrics PresentationMetrics { get; } = new();

    /// <summary>
    /// True if the last frame's flush was aborted mid-way because the console
    /// size changed during rendering. The caller should discard and re-render.
    /// </summary>
    public bool FrameWasInterrupted { get; private set; }

    public ScreenRenderer(IConsoleDriver driver)
        : this(driver, ReadPresentationMode())
    {
    }

    internal ScreenRenderer(IConsoleDriver driver, ScreenPresentationMode presentationMode)
    {
        _driver = driver;
        _presentationMode = presentationMode;

    }

    public ConsoleViewport GetViewport() => _driver.GetViewport();

    public ConsoleSize GetSize() => _driver.GetSize();

    public string ConsoleDriverName => _driver.GetType().Name;

    public IConsoleInputDiagnostics? GetInputDiagnostics() =>
        _driver as IConsoleInputDiagnostics;

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

    public bool TryIsViewportAtBottom(out bool isAtBottom) =>
        _driver.TryIsViewportAtBottom(out isAtBottom);

    /// <summary>
    /// Marks the physical output surface as unknown so the next frame is rendered in full.
    /// </summary>
    public void InvalidatePhysicalOutput()
    {
        if (_frameActive)
            throw new InvalidOperationException("Cannot invalidate physical output during an active render frame.");

        _frontBufferKnown = false;
        _frontBufferViewport = null;
        _forceFullFrame = true;
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

    public ConsoleSize Size => _frameActive ? _frameSize : _driver.GetSize();

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
        return BeginFrame(viewport, FrameOwnership.Application);
    }

    public IDisposable BeginFrameFromCurrentViewportCapture()
    {
        if (_frameActive)
            throw new InvalidOperationException("A render frame is already active.");

        var viewport = _driver.GetViewport();
        var snapshot = _driver.Capture(new Rect(0, 0, viewport.Width, viewport.Height));
        viewport = snapshot.Viewport;
        var size = viewport.Size;
        EnsureBuffers(size);
        CopySnapshotToBuffer(_frontBuffer!, snapshot);
        _frontBufferKnown = true;
        _frontBufferViewport = viewport;
        _forceFullFrame = false;

        return BeginFrame(viewport, FrameOwnership.CapturedExternalSurface);
    }

    private IDisposable BeginFrame(ConsoleViewport viewport, FrameOwnership ownership)
    {
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
        _frameOwnership = ownership;
        _frameActive = true;
        if (TerminalTrace.Enabled)
            TerminalTrace.Write("renderer", $"FRAME BEGIN ownership={ownership} viewport={viewport}");
        FrameWasInterrupted = false;

        return new Frame(this);
    }

    public void Write(int x, int y, string text, CellStyle style) =>
        Write(x, y, text.AsSpan(), style);

    public void WriteForced(int x, int y, string text, CellStyle style) =>
        WriteForced(x, y, text.AsSpan(), style);

    public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return;

        var size = _frameActive ? _frameSize : _driver.GetSize();
        if (y >= size.Height || x >= size.Width)
            return;

        string clipped = ConsoleTextMetrics.TruncateToCells(text.ToString(), size.Width - x);

        if (!_frameActive)
        {
            Write(x, y, clipped, style);
            return;
        }

        EnsureBuffers(size);
        WriteToBuffer(_backBuffer!, x, y, clipped, style);
        int writtenCells = ConsoleTextMetrics.GetCellWidth(clipped);
        for (int i = 0; i < writtenCells; i++)
        {
            _frontBuffer![y, x + i] = new BufferCell
            {
                Foreground = style.Foreground,
                Background = style.Background,
                Attributes = style.Attributes,
            };
        }
    }

    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style)
    {
        if (text.IsEmpty || x < 0 || y < 0)
            return;

        var size = _frameActive ? _frameSize : _driver.GetSize();
        if (y >= size.Height || x >= size.Width)
            return;

        string clipped = ConsoleTextMetrics.TruncateToCells(text.ToString(), size.Width - x);

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
        if (_frameActive)
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

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var frameStopwatch = Stopwatch.StartNew();
        try
        {
            var present = FlushFrame();

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

            frameStopwatch.Stop();
            PresentationMetrics.Add(present with
            {
                FrameTime = frameStopwatch.Elapsed,
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            });
        }
        finally
        {
            _frameOwnership = FrameOwnership.Application;
            _frameActive = false;
        }
    }

    private FramePresentationMeasurement FlushFrame()
    {
        var presentStopwatch = Stopwatch.StartNew();
        int dirtyCells = 0;
        int dirtyRows = 0;
        int outputCalls = 0;
        int viewportQueryCalls = 0;
        int transmittedCells = 0;
        int transmittedCharacters = 0;
        int transmittedBytes = 0;

        FramePresentationMeasurement Complete()
        {
            presentStopwatch.Stop();
            return new FramePresentationMeasurement(
                FrameTime: default,
                PresentTime: presentStopwatch.Elapsed,
                DirtyCells: dirtyCells,
                DirtyRows: dirtyRows,
                OutputCalls: outputCalls,
                ViewportQueryCalls: viewportQueryCalls,
                TransmittedCells: transmittedCells,
                TransmittedCharacters: transmittedCharacters,
                TransmittedBytes: transmittedBytes,
                AllocatedBytes: 0);
        }

        if (_frontBuffer is null || _backBuffer is null)
            return Complete();

        bool forceFull = !_frontBufferKnown || _forceFullFrame;
        int height = _bufferSize.Height;
        int width = _bufferSize.Width;

        if (_presentationMode != ScreenPresentationMode.Current &&
            !ViewportMatchesFrame(ref viewportQueryCalls))
        {
            InterruptFrame();
            return Complete();
        }

        CountDirtyCells(forceFull, ref dirtyCells, ref dirtyRows);
        if (dirtyCells == 0)
            return Complete();

        if (CanWriteBatch(ConsoleFrameWriteCapabilities.WindowsCells) &&
            _frameOwnership == FrameOwnership.Application &&
            _presentationMode == ScreenPresentationMode.Win32FrameBatch)
        {
            if (TerminalTrace.Enabled)
            {
                TerminalTrace.Write(
                    "renderer",
                    $"WRITE type=Win32FrameBatch ownership={_frameOwnership} region=(0,0,{width},{height})");
            }
            FillPresentationBuffer(0, 0, width, height);
            if (!WriteCells(0, 0, width, height))
            {
                InterruptFrame();
                return Complete();
            }

            outputCalls++;
            transmittedCells += width * height;
            transmittedCharacters += width * height;
            transmittedBytes += PresentationBytes(width * height);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                if (_presentationMode == ScreenPresentationMode.Current &&
                    !ViewportMatchesFrame(ref viewportQueryCalls))
                {
                    InterruptFrame();
                    return Complete();
                }

                bool dirtyRow = RowIsDirty(y, forceFull);
                if (!dirtyRow)
                    continue;

                if (CanWriteBatch(ConsoleFrameWriteCapabilities.WindowsCells) &&
                    _presentationMode == ScreenPresentationMode.Win32RowBatch)
                {
                    FillPresentationBuffer(0, y, width, 1);
                    if (!WriteCells(0, y, width, 1))
                    {
                        InterruptFrame();
                        return Complete();
                    }

                    outputCalls++;
                    transmittedCells += width;
                    transmittedCharacters += width;
                    transmittedBytes += PresentationBytes(width);
                    continue;
                }

                if (CanWriteBatch(ConsoleFrameWriteCapabilities.VirtualTerminalCells) &&
                    _presentationMode == ScreenPresentationMode.VtBatch)
                {
                    continue;
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

                    int end = x;
                    while (start > 0 && _backBuffer[y, start].IsContinuation)
                        start--;

                    var chars = new System.Text.StringBuilder();
                    for (int i = start; i < end; i++)
                        if (!_backBuffer[y, i].IsContinuation)
                            chars.Append(_backBuffer[y, i].Text);

                    string text = chars.ToString();
                    if (!_driver.TryWriteAtViewport(_frameViewport, start, y, text, first.Foreground, first.Background, first.Attributes))
                    {
                        InterruptFrame();
                        return Complete();
                    }

                    outputCalls++;
                    if (TerminalTrace.Enabled)
                    {
                        TerminalTrace.Write(
                            "renderer",
                            $"WRITE type=dirty ownership={_frameOwnership} region=({start},{y},{end - start},1)");
                    }
                    transmittedCharacters += chars.Length;
                    transmittedCells += end - start;
                    transmittedBytes += System.Text.Encoding.UTF8.GetByteCount(text);
                }
            }

            if (CanWriteBatch(ConsoleFrameWriteCapabilities.VirtualTerminalCells) &&
                _presentationMode == ScreenPresentationMode.VtBatch)
            {
                BuildDirtyPresentationRuns(forceFull);
                if (!((IConsoleFrameWriter)_driver).TryWriteDirtyCellsAtViewport(
                    _frameViewport, CollectionsMarshal.AsSpan(_presentationRuns), _presentationBuffer.AsSpan(0, _presentationBufferCount)))
                {
                    InterruptFrame();
                    return Complete();
                }

                outputCalls++;
                transmittedCells += _presentationBufferCount;
                transmittedCharacters += _presentationBufferCount;
                transmittedBytes += PresentationBytes(_presentationBufferCount);
            }
        }

        if (!ViewportMatchesFrame(ref viewportQueryCalls))
        {
            InterruptFrame();
            return Complete();
        }

        Array.Copy(_backBuffer, _frontBuffer, _backBuffer.Length);
        _frontBufferKnown = true;
        _frontBufferViewport = _frameViewport;
        _forceFullFrame = false;
        return Complete();
    }

    private bool CanWriteBatch(ConsoleFrameWriteCapabilities capability) =>
        _driver is IConsoleFrameWriter { Capabilities: var capabilities } &&
        (capabilities & capability) != 0;

    private bool ViewportMatchesFrame(ref int viewportQueryCalls)
    {
        viewportQueryCalls++;
        return _driver.GetViewport() == _frameViewport;
    }

    private bool WriteCells(int x, int y, int width, int height) =>
        ((IConsoleFrameWriter)_driver).TryWriteCellsAtViewport(
            _frameViewport, x, y, width, height, _presentationBuffer.AsSpan(0, width * height));

    private void FillPresentationBuffer(int x, int y, int width, int height)
    {
        int count = width * height;
        if (_presentationBuffer.Length < count)
            _presentationBuffer = new ConsoleOutputCell[count];

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                var cell = _backBuffer![y + row, x + column];
                char character = cell.IsContinuation
                    ? ContinuationCharacter(x + column, y + row)
                    : cell.Text[0];
                _presentationBuffer[row * width + column] = new ConsoleOutputCell(
                    character, cell.Foreground, cell.Background, cell.Attributes);
            }
        }
    }

    private void BuildDirtyPresentationRuns(bool forceFull)
    {
        _presentationRuns.Clear();
        int cellCount = 0;
        for (int y = 0; y < _bufferSize.Height; y++)
        {
            int x = 0;
            while (x < _bufferSize.Width)
            {
                if (!IsDirty(y, x, forceFull))
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < _bufferSize.Width && IsDirty(y, x, forceFull))
                    x++;

                if (start > 0 && _backBuffer![y, start].IsContinuation)
                    start--;

                int length = x - start;
                cellCount += length;
            }
        }

        if (_presentationBuffer.Length < cellCount)
            _presentationBuffer = new ConsoleOutputCell[cellCount];

        _presentationBufferCount = cellCount;

        int offset = 0;
        for (int y = 0; y < _bufferSize.Height; y++)
        {
            int x = 0;
            while (x < _bufferSize.Width)
            {
                if (!IsDirty(y, x, forceFull))
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < _bufferSize.Width && IsDirty(y, x, forceFull))
                    x++;

                if (start > 0 && _backBuffer![y, start].IsContinuation)
                    start--;

                int length = x - start;
                _presentationRuns.Add(new ConsoleOutputRun(start, y, offset, length));
                for (int column = 0; column < length; column++)
                {
                    var cell = _backBuffer![y, start + column];
                    char character = cell.IsContinuation
                        ? ContinuationCharacter(start + column, y)
                        : cell.Text[0];
                    _presentationBuffer[offset + column] = new ConsoleOutputCell(
                        character, cell.Foreground, cell.Background, cell.Attributes);
                }

                offset += length;
            }
        }

    }

    private char ContinuationCharacter(int x, int y)
    {
        var previous = _backBuffer![y, x - 1];
        return previous.Text.Length > 1 ? previous.Text[1] : ' ';
    }

    private int PresentationBytes(int count)
    {
        int bytes = 0;
        for (int i = 0; i < count; i++)
            bytes += Utf8Length(_presentationBuffer[i].Character);
        return bytes;
    }

    private static int Utf8Length(char character) => character switch
    {
        <= '\x7f' => 1,
        <= '\x7ff' => 2,
        _ => 3,
    };

    private void CountDirtyCells(bool forceFull, ref int dirtyCells, ref int dirtyRows)
    {
        for (int y = 0; y < _bufferSize.Height; y++)
        {
            bool rowDirty = false;
            for (int x = 0; x < _bufferSize.Width; x++)
            {
                if (!IsDirty(y, x, forceFull))
                    continue;

                dirtyCells++;
                rowDirty = true;
            }

            if (rowDirty)
                dirtyRows++;
        }
    }

    private bool RowIsDirty(int y, bool forceFull)
    {
        for (int x = 0; x < _bufferSize.Width; x++)
            if (IsDirty(y, x, forceFull))
                return true;

        return false;
    }

    private static ScreenPresentationMode ReadPresentationMode() =>
        ScreenPresentationMode.Win32FrameBatch;

    private string FormatPresentationReport()
    {
        var report = PresentationMetrics.CreateReport();
        return $"render-presentation mode={_presentationMode} frames={report.Frames} " +
               $"present-p50={report.PresentP50.TotalMilliseconds:F3}ms " +
               $"present-p95={report.PresentP95.TotalMilliseconds:F3}ms " +
               $"present-p99={report.PresentP99.TotalMilliseconds:F3}ms " +
               $"frame-p50={report.FrameP50.TotalMilliseconds:F3}ms " +
               $"frame-p95={report.FrameP95.TotalMilliseconds:F3}ms " +
               $"frame-p99={report.FrameP99.TotalMilliseconds:F3}ms " +
               $"output-calls/frame={report.OutputCallsPerFrame:F2} " +
               $"allocations/frame={report.AllocatedBytesPerFrame:F0}B";
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

    private static BufferCell[,] CreateBuffer(ConsoleSize size)
    {
        var buffer = new BufferCell[size.Height, size.Width];
        FillBuffer(buffer, 0, 0, size.Width, size.Height, CellStyle.Default);
        return buffer;
    }

    private static void WriteToBuffer(
        BufferCell[,] buffer,
        int x,
        int y,
        ReadOnlySpan<char> text,
        CellStyle style)
    {
        int column = x;
        foreach (var rune in text.ToString().EnumerateRunes())
        {
            int width = ConsoleTextMetrics.GetCellWidth(rune);
            if (width == 0)
            {
                int baseColumn = column - 1;
                while (baseColumn >= x && buffer[y, baseColumn].IsContinuation)
                    baseColumn--;
                if (baseColumn >= x)
                    buffer[y, baseColumn].Text += rune.ToString();
                continue;
            }

            if (column + width > buffer.GetLength(1))
                break;

            buffer[y, column] = new BufferCell(rune.ToString(), false, style.Foreground, style.Background, style.Attributes);
            for (int i = 1; i < width; i++)
                buffer[y, column + i] = new BufferCell(string.Empty, true, style.Foreground, style.Background, style.Attributes);
            column += width;
        }
    }

    private static void FillBuffer(BufferCell[,] buffer, int x, int y, int width, int height, CellStyle style)
    {
        var cell = new BufferCell(" ", false, style.Foreground, style.Background, style.Attributes);

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

    private void CopySnapshotToBuffer(BufferCell[,] buffer, ScreenSnapshot snapshot)
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
                var cell = snapshot.Cells[row, col];
                buffer[y, x] = new BufferCell(cell.Character.ToString(), false, cell.Foreground, cell.Background, cell.Attributes);
            }
        }
    }

    private void ApplyCursorVisible(bool visible)
    {
        if (!visible)
        {
            _driver.SetCursorVisible(false);
            _cursorVisible = false;
            return;
        }

        if (_cursorVisible == visible)
            return;

        _driver.SetCursorVisible(visible);
        _cursorVisible = visible;
    }

    private static bool SameCell(BufferCell left, BufferCell right) =>
        left.Text == right.Text &&
        left.IsContinuation == right.IsContinuation &&
        SameStyle(left, right);

    private static bool SameStyle(BufferCell left, BufferCell right) =>
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

    private struct BufferCell
    {
        public BufferCell(string text, bool isContinuation, ConsoleColor foreground, ConsoleColor background, TextAttributes attributes)
        {
            Text = text;
            IsContinuation = isContinuation;
            Foreground = foreground;
            Background = background;
            Attributes = attributes;
        }

        public string Text { get; set; }
        public bool IsContinuation { get; set; }
        public ConsoleColor Foreground { get; set; }
        public ConsoleColor Background { get; set; }
        public TextAttributes Attributes { get; set; }
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
