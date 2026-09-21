using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Text;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies TextFileReader encoding detection and F3 viewer behavior.
/// </summary>
public class FileViewerTests : IDisposable
{
    private const long LargeTestFileSize = 1L * 1024 * 1024 + 1024;

    private readonly string _tempDir;

    public FileViewerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarViewerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Encoding detection ────────────────────────────────────────────────────

    [Fact]
    public void EncodingDetector_DetectsCommonInputs()
    {
        var utf8Bom = TextEncodingDetector.Detect([0xEF, 0xBB, 0xBF, 0x41]);
        Assert.Equal(65001, utf8Bom.Encoding.CodePage);
        Assert.Equal(3, utf8Bom.ContentStartLength);
        Assert.Equal("UTF-8 BOM", utf8Bom.DisplayName);

        var utf16LeBom = TextEncodingDetector.Detect([0xFF, 0xFE, 0x41, 0x00]);
        Assert.Equal(1200, utf16LeBom.Encoding.CodePage);
        Assert.Equal(2, utf16LeBom.ContentStartLength);

        var utf16BeBom = TextEncodingDetector.Detect([0xFE, 0xFF, 0x00, 0x41]);
        Assert.Equal(1201, utf16BeBom.Encoding.CodePage);
        Assert.Equal(2, utf16BeBom.ContentStartLength);

        var utf8 = TextEncodingDetector.Detect(Encoding.UTF8.GetBytes("plain \u20AC"));
        Assert.Equal(65001, utf8.Encoding.CodePage);
        Assert.False(utf8.HasByteOrderMark);

        var fallback = TextEncodingDetector.Detect([0xE9]);
        Assert.Equal(TextEncodingSelectionKind.Automatic, fallback.Selection.Kind);
        Assert.NotEqual(65001, fallback.Encoding.CodePage);

        var binary = TextEncodingDetector.Detect([0x00, 0x01, 0x41]);
        Assert.True(binary.IsBinary);
    }

    [Fact]
    public void ReadLines_ReadsPlainUtf8()
    {
        string path = Write("utf8.txt", "Line 1\nLine 2\nLine 3", Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Line 1", lines[0]);
        Assert.Equal("Line 3", lines[2]);
    }

    [Fact]
    public void ReadLines_HandlesUtf8Bom()
    {
        string path = WritePath("bom.txt");
        File.WriteAllText(path, "Hello BOM", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Single(lines);
        Assert.Equal("Hello BOM", lines[0]); // BOM stripped by StreamReader
    }

    [Fact]
    public void ReadLines_FallsBackForNonUtf8()
    {
        // 0xE9 = é in Windows-1252, invalid as standalone UTF-8 byte
        string path = WritePath("ansi.txt");
        File.WriteAllBytes(path, [0xE9, 0x0D, 0x0A]); // é + CRLF

        // Must not throw
        string[] lines = TextFileReader.ReadLines(path);
        Assert.Single(lines);
    }

    // -- Unified file viewer --------------------------------------------------

    [Fact]
    public void Show_SmallTextFileUsesUnifiedStreamingViewer()
    {
        string path = Write("small-viewer.txt", "first\nsecond", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT", writes);
        Assert.Contains("first", writes);
    }

    [Fact]
    public void Show_HidesTerminalCursorWhileViewerIsActive()
    {
        string path = Write("cursorless-viewer.txt", "content", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.BeforeReadInput = current => Assert.False(current.CursorVisible);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);
    }

    [Fact]
    public void Show_OpensFileLargerThanOldLimit()
    {
        string path = WriteLargeTextFile(
            "larger-than-old-limit.txt",
            TextFileReader.MaxFileSizeBytes + 1024,
            "tail after old limit");
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.DoesNotContain("File too large", writes);
        Assert.Contains("larger-than-old-limit.txt", writes);
    }

    [Fact]
    public void Show_EndOnLargeTextFileShowsTail()
    {
        string path = WriteLargeTextFile(
            "large-tail.txt",
            LargeTestFileSize,
            "tail-large-file");
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("tail-large-file", WrittenText(driver));
    }

    [Fact]
    public void Show_BinaryFileUsesHexModeByDefault()
    {
        string path = WritePath("binary.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x41, 0x42]);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("HEX", writes);
        Assert.Contains("00000000", writes);
    }

    [Theory]
    [InlineData(ConsoleKey.H, 'h')]
    [InlineData(ConsoleKey.F4, '\0')]
    public void Show_TextFileCanToggleToHexMode(ConsoleKey key, char keyChar)
    {
        string path = Write("toggle-text.txt", "ABC", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(key, keyChar));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("HEX", writes);
        Assert.Contains("41 42 43", writes);
    }

    [Fact]
    public void Show_F3ClosesViewer()
    {
        string path = Write("f3-close.txt", "closed by f3", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F3));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("closed by f3", WrittenText(driver));
    }

    [Fact]
    public void Show_RendersViewerFunctionKeyBarActions()
    {
        string path = Write("viewer-keybar-layout.txt", "text", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 120, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string row = ComposeRow(driver, y: 9, width: 120);
        Assert.Contains("1Help", row);
        Assert.Contains("3Close", row);
        Assert.Contains("4Hex", row);
        Assert.Contains("6Edit", row);
        Assert.Contains("7Find", row);
        Assert.Contains("8Enc", row);
        Assert.Contains("10Close", row);
    }

    [Fact]
    public void Show_FunctionKeyBarMouseClickF4TogglesHexMode()
    {
        string path = Write("viewer-keybar-mouse-f4.txt", "A B", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 120, height: 10);
        driver.EnqueueInput(LeftMouse(x: 30, y: 9));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("HEX", WrittenText(driver));
    }

    [Fact]
    public void Show_FunctionKeyBarMouseClickF10ClosesViewer()
    {
        string path = Write("viewer-keybar-mouse-f10.txt", "text", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 120, height: 10);
        driver.EnqueueInput(LeftMouse(x: 90, y: 9));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("10Close", ComposeRow(driver, y: 9, width: 120));
    }

    [Fact]
    public void Show_MouseWheelScrollsTextDown()
    {
        string path = Write("viewer-wheel.txt", string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")), new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueInput(MouseWheelDown());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("line4", WrittenText(driver));
    }

    [Fact]
    public void Show_MouseWheelOutsideContentDoesNotScroll()
    {
        string path = Write("viewer-wheel-outside.txt", string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")), new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueInput(new MouseConsoleInputEvent(0, 7, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("line1", content);
        Assert.DoesNotContain("line7", content);
    }

    [Fact]
    public void Show_MouseWheelRedrawDoesNotRewriteFunctionKeyBar()
    {
        string path = Write("viewer-wheel-keybar.txt", string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")), new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        var wheelRedrawWrites = Array.Empty<FakeConsoleDriver.WriteRecord>();
        driver.BeforeReadInput = firstRead =>
        {
            firstRead.ClearRecordedOperations();
            firstRead.BeforeReadInput = secondRead =>
            {
                wheelRedrawWrites = secondRead.WriteRecords.ToArray();
            };
        };
        driver.EnqueueInput(MouseWheelDown());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.DoesNotContain(wheelRedrawWrites, record => record.Y == 7);
    }

    [Fact]
    public void Show_BinaryFileCanToggleToTextMode()
    {
        string path = WritePath("toggle-binary.bin");
        File.WriteAllBytes(path, [0x41, 0x00, 0x42, 0x0A]);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.H, 'h'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT", writes);
        Assert.Contains("A B", writes);
    }

    [Fact]
    public void SanitizeTextForConsole_ReplacesControlCharacters()
    {
        string sanitized = LargeFileViewer.SanitizeTextForConsole("A\u001B[31mB\u0007C\0D\tE");

        Assert.Equal("A [31mB C D    E", sanitized);
    }

    [Fact]
    public void Show_TextHorizontalScrollUsesTerminalCellsForWideCharacters()
    {
        string path = Write("wide-scroll.txt", "界ABCD", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 8, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Equal('A', driver.GetCell(0, 1).Character);
        Assert.Equal('B', driver.GetCell(1, 1).Character);
    }

    [Fact]
    public void Show_WrapsWideTextWithinTerminalCellViewport()
    {
        string path = Write("wide-wrap.txt", "界AB", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 3, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string firstContentWrite = driver.WriteRecords
            .First(record => record.Y == 1 && record.Text.Contains('界'))
            .Text;
        Assert.Equal(3, ConsoleTextMetrics.GetCellWidth(firstContentWrite));
        Assert.Equal("界A", firstContentWrite.TrimEnd());
    }

    [Fact]
    public void Show_SearchHighlightAccountsForWideTextAndExpandedTab()
    {
        string path = Write("wide-tab-find.txt", "界\ttarget", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 20, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "target");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotEqual(driver.GetCell(5, 1).Foreground, driver.GetCell(6, 1).Foreground);
        Assert.Equal('t', driver.GetCell(6, 1).Character);
    }

    [Fact]
    public void Show_HeaderFitsUnicodeFilePathIntoViewport()
    {
        string path = Write("界界界-long-name.txt", "text", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 24, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.All(driver.WriteRecords.Where(record => record.Y == 0),
            record => Assert.True(ConsoleTextMetrics.GetCellWidth(record.Text) <= 24));
    }

    [Fact]
    public void Show_FKeyCyclesWatchAndTailStatus()
    {
        string path = WriteLargeTextFile(
            "large-live.txt",
            LargeTestFileSize,
            "tail-live-file");
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("WATCH", writes);
        Assert.Contains("TAIL", writes);
        Assert.Contains("tail-live-file", writes);
    }

    [Fact]
    public void Show_EndEnablesTailForLocalFile()
    {
        string path = Write("end-tail.txt", "first\nsecond\nthird", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_EndOnVirtualSourceDoesNotEnableTail()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var reader = new MemoryFileByteReader(Encoding.UTF8.GetBytes("first\nsecond\n"));

        FileViewerFor(new ScreenRenderer(driver)).Show("remote.txt", reader);

        Assert.DoesNotContain("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_WrappedEndShowsFinalSegmentsOfLongLine()
    {
        string path = Write(
            "wrapped-tail.txt",
            new string('x', 96) + "TAIL",
            new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 10, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_F5KeepsTailAtNewPresentationMaximum()
    {
        string path = Write(
            "tail-presentation.md",
            "[label](https://example.test/" + new string('x', 180) + ") RAW-END",
            new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 20, height: 7);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 20, 5));
        Assert.Contains("RAW-END", content);
        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_PageDownClampsToLastFullPage()
    {
        string path = Write(
            "page-down-clamp.txt",
            string.Join('\n', Enumerable.Range(1, 8).Select(i => $"line{i}")),
            new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("line3", content);
        Assert.Contains("line8", content);
    }

    [Fact]
    public void Show_F2TogglesWrapStatus()
    {
        string path = Write("wrap.txt", "alpha beta gamma", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("WRAP-W", WrittenText(driver));
    }

    [Fact]
    public void RandomAccessFileByteReader_DoesNotHoldPersistentHandle()
    {
        string path = Write("non-blocking-reader.txt", "content", new UTF8Encoding(false));
        using var reader = new RandomAccessFileByteReader(path);

        using var exclusive = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        Assert.True(exclusive.CanRead);
    }

    [Fact]
    public async Task RandomAccessFileByteReader_ReadsReplacementAtSamePath()
    {
        string path = WritePath("replace-reader.txt");
        File.WriteAllBytes(path, "AAAA"u8.ToArray());
        using var reader = new RandomAccessFileByteReader(path);
        var buffer = new byte[4];

        Assert.Equal(4, await reader.ReadAsync(0, buffer));
        Assert.Equal("AAAA", Encoding.ASCII.GetString(buffer));

        string replacement = WritePath("replace-reader.tmp");
        File.WriteAllBytes(replacement, "BBBB"u8.ToArray());
        File.Move(replacement, path, overwrite: true);

        Array.Clear(buffer);
        Assert.Equal(4, await reader.ReadAsync(0, buffer));
        Assert.Equal("BBBB", Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public async Task BlockCache_ClearDropsSameLengthStaleBlock()
    {
        string path = WritePath("cache-overwrite.txt");
        File.WriteAllBytes(path, "AAAA"u8.ToArray());
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader, blockSize: 4, capacity: 2);

        Assert.Equal("AAAA", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));

        File.WriteAllBytes(path, "BBBB"u8.ToArray());
        Assert.Equal("AAAA", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));

        cache.Clear();
        Assert.Equal("BBBB", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));
    }

    [Fact]
    public void LocalFileMonitor_DirtySignalForcesRefreshWithUnchangedMetadata()
    {
        var snapshot = new LocalFileSnapshot(
            Exists: true,
            Length: 4,
            LastWriteTimeUtc: new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(LocalFileMonitor.ShouldRefresh(snapshot, snapshot, watcherDirty: false));
        Assert.True(LocalFileMonitor.ShouldRefresh(snapshot, snapshot, watcherDirty: true));
        Assert.True(LocalFileMonitor.ShouldRefresh(
            snapshot,
            snapshot with { Length = 5 },
            watcherDirty: false));
    }

    [Fact]
    public void LocalFileMonitoringSession_IsLazyAndReusesActiveMonitor()
    {
        string path = Write("monitor-lifecycle.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);

        Assert.False(monitoring.IsActive);
        Assert.False(monitoring.PendingRefresh);
        Assert.Equal(0, factory.CreateCount);

        monitoring.Activate();

        Assert.True(monitoring.IsActive);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);

        long initialRefresh = monitoring.BeginRefresh();
        monitoring.Commit(snapshot, initialRefresh);
        Assert.False(monitoring.PendingRefresh);

        monitoring.Activate();
        Assert.Equal(1, factory.CreateCount);

        FakeLocalFileMonitor first = factory.Current;
        monitoring.Deactivate();

        Assert.True(first.IsDisposed);
        Assert.False(monitoring.IsActive);
        Assert.False(monitoring.PendingRefresh);

        monitoring.Activate();

        Assert.Equal(2, factory.CreateCount);
        Assert.True(monitoring.PendingRefresh);
        Assert.NotSame(first, factory.Current);
    }

    [Fact]
    public void LocalFileMonitoringSession_NotificationDuringRefreshRemainsPending()
    {
        string path = Write("monitor-race.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        factory.Current.MarkDirty();
        monitoring.DetectChanges();
        Assert.True(monitoring.PendingRefresh);

        long refreshVersion = monitoring.BeginRefresh();
        factory.Current.MarkDirty();
        monitoring.Commit(snapshot, refreshVersion);

        Assert.True(monitoring.PendingRefresh);

        long secondRefreshVersion = monitoring.BeginRefresh();
        monitoring.Commit(snapshot, secondRefreshVersion);

        Assert.False(monitoring.PendingRefresh);
    }

    [Fact]
    public void LocalFileMonitoringSession_PollingWorksWithoutNotifications()
    {
        string path = Write("monitor-polling-only.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory(notificationsAvailable: false);

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        File.AppendAllText(path, "B", new UTF8Encoding(false));
        monitoring.DetectChanges();

        Assert.True(monitoring.IsActive);
        Assert.False(monitoring.NotificationsAvailable);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);

        monitoring.DetectChanges();
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void LocalFileMonitoringSession_NotificationBackendErrorRequestsResync()
    {
        string path = Write("monitor-error.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        factory.Current.FailNotifications();
        monitoring.DetectChanges();

        Assert.True(monitoring.IsActive);
        Assert.False(monitoring.NotificationsAvailable);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public async Task LineScanner_VeryLongPhysicalLineUsesRealEofAndBoundedCapture()
    {
        const int oldScanLimit = 4 * 1024 * 1024;
        string path = WritePath("very-long-physical-line.txt");
        byte[] content = Enumerable.Repeat((byte)'x', oldScanLimit + 4096).ToArray();
        File.WriteAllBytes(path, content);

        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader);
        var scanner = await LineScanner.CreateAsync(cache, reader);

        ScannedLines scanned = await scanner.ReadLinesAsync(
            scanner.ContentStartOffset,
            lineCount: 2,
            maxBytesPerLine: 256);

        ScannedLine line = Assert.Single(scanned.Lines);
        Assert.Equal(content.LongLength, line.NextOffset);
        Assert.Equal(content.LongLength, scanned.NextOffset);
        Assert.True(line.Text.Length <= 256);
    }

    [Fact]
    public async Task LineScanner_ReadLines_HandlesUtf16Bom()
    {
        string path = Write("utf16-large-path.txt", "alpha\r\nbeta\r\n", Encoding.Unicode);
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader, blockSize: 8, capacity: 2);
        var scanner = await LineScanner.CreateAsync(cache, reader);

        var lines = await scanner.ReadLinesAsync(scanner.ContentStartOffset, 2, maxBytesPerLine: 256);

        Assert.Equal("utf-16", scanner.Encoding.WebName);
        Assert.Equal(["alpha", "beta"], lines.Lines.Select(line => line.Text).ToArray());
    }

    [Theory]
    [InlineData(65001, "UTF-8", "hello \u20AC")]
    [InlineData(1200, "UTF-16 LE", "hello \u20AC")]
    [InlineData(1201, "UTF-16 BE", "hello \u20AC")]
    [InlineData(1251, "Windows-1251", "\u041F\u0440\u0438\u0432\u0435\u0442")]
    [InlineData(1252, "Windows-1252", "caf\u00E9")]
    [InlineData(866, "CP866", "\u041F\u0440\u0438\u0432\u0435\u0442")]
    public async Task LineScanner_ReadLines_UsesExplicitEncoding(int codePage, string displayName, string text)
    {
        TextEncodingDetector.EnsureCodePagesProviderRegistered();
        string path = WritePath($"explicit-{codePage}.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding(codePage).GetBytes(text + "\n"));
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader, blockSize: 5, capacity: 2);
        var scanner = await LineScanner.CreateAsync(cache, reader, TextEncodingSelection.Explicit(codePage));

        var lines = await scanner.ReadLinesAsync(scanner.ContentStartOffset, 1, maxBytesPerLine: 256);

        Assert.Equal(displayName, scanner.EncodingDisplayName);
        Assert.Equal(text, lines.Lines[0].Text);
    }

    [Fact]
    public async Task LineScanner_ReadLines_ReplacesDamagedText()
    {
        string path = WritePath("damaged.txt");
        File.WriteAllBytes(path, [0xC3, 0x28, 0x0A]);
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader, blockSize: 4, capacity: 2);
        var scanner = await LineScanner.CreateAsync(cache, reader);

        var lines = await scanner.ReadLinesAsync(0, 1, maxBytesPerLine: 16);

        Assert.Single(lines.Lines);
        Assert.NotNull(lines.Lines[0].Text);
    }

    [Fact]
    public void Show_F8SelectionChangesDisplayedEncoding()
    {
        TextEncodingDetector.EnsureCodePagesProviderRegistered();
        string expected = "\u041F\u0440\u0438\u0432\u0435\u0442";
        string path = WritePath("manual-1251.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes(expected + "\n"));
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true));
        for (int i = 0; i < 5; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT Windows-1251", writes);
        Assert.Contains(expected, writes);
    }

    [Fact]
    public void Show_F8SelectionPreviewsEncodingBeforeEnter()
    {
        TextEncodingDetector.EnsureCodePagesProviderRegistered();
        string expected = "\u041F\u0440\u0438\u0432\u0435\u0442";
        string path = WritePath("preview-cp866.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding(866).GetBytes(expected + "\n"));
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true));
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT CP866", writes);
        Assert.Contains(expected, writes);
    }

    [Fact]
    public void Show_F8ExplicitEncodingSwitchesBinaryHexToText()
    {
        string path = WritePath("binary-to-text.bin");
        File.WriteAllBytes(path, [0x41, 0x00, 0x42, 0x0A]);
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true));
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT CP866", writes);
        Assert.Contains("A B", writes);
    }

    [Fact]
    public void Show_F8CyclesCommonEncoding()
    {
        string path = Write("cycle-encoding.txt", "ABC", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F8));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("TEXT UTF-8", WrittenText(driver));
    }

    [Fact]
    public void Show_AltF8UsesGoToDialog()
    {
        string path = Write("alt-f8-go.txt", "first\nsecond\nthird", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F8, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.D2, '2'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        Assert.Contains("second", WrittenText(driver));
    }

    [Fact]
    public void Show_F7FindsTextAndShowsFindStatus()
    {
        string path = Write("find.txt", "alpha\ntargetf7\nomega", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "targetf7");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("targetf7", writes);
        Assert.Contains("FIND", writes);
    }

    [Fact]
    public async Task ViewerSearchEngine_FindsTextForward()
    {
        string path = Write("find-engine.txt", "alpha\nenginehit\nomega", new UTF8Encoding(false));
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader);
        var scanner = await LineScanner.CreateAsync(cache, reader);
        var state = new LargeFileViewerState(cache, scanner);

        var match = ViewerSearchEngine.Find(
            reader,
            state,
            new ViewerSearchRequest("enginehit", CaseSensitive: false, WholeWords: false, UseRegex: false, SearchHex: false),
            searchBackward: false);

        Assert.NotNull(match);
        Assert.Equal("enginehit", match.MatchedText);
    }

    [Fact]
    public void Show_CtrlCCopiesCurrentSearchMatch()
    {
        string path = Write("find-copy.txt", "alpha copytoken omega", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "copytoken");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.C, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var clipboard = new FakeTextClipboard();
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path, new LargeFileViewerOptions { Clipboard = clipboard });

        Assert.Equal("copytoken", clipboard.Text);
    }

    [Fact]
    public void Show_F7FindsHexSequenceInHexMode()
    {
        string path = WritePath("find-hex.bin");
        File.WriteAllBytes(path, [0x00, 0x41, 0x42, 0x43]);
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "41 42");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("FIND", writes);
        Assert.Contains("41 42 43", writes);
    }

    [Fact]
    public void Show_PlusMovesToNextSiblingFile()
    {
        string first = Write("first-sibling.txt", "first file", new UTF8Encoding(false));
        string second = Write("second-sibling.txt", "second file", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.Add, '+'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        string? changedPath = null;
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(first, new LargeFileViewerOptions
        {
            FilePaths = [first, second],
            CurrentFileIndex = 0,
            CurrentFileChanged = path => changedPath = path,
        });

        Assert.Equal(second, changedPath);
        Assert.Contains("second file", WrittenText(driver));
    }

    [Fact]
    public void Show_F6RefreshesEditedFileWhileLiveModeIsOff()
    {
        string path = Write("viewer-edit-refresh.txt", "AAAA", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.F6));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path, new LargeFileViewerOptions
        {
            EditFile = value => File.WriteAllText(value, "BBBB", new UTF8Encoding(false)),
        });

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("BBBB", content);
        Assert.DoesNotContain("WATCH", WrittenText(driver));
        Assert.DoesNotContain("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_F6InvokesEditorLauncher()
    {
        string path = Write("viewer-edit.txt", "edit me", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F6));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        string? editedPath = null;
        var screen = new ScreenRenderer(driver);

        FileViewerFor(screen).Show(path, new LargeFileViewerOptions
        {
            EditFile = value => editedPath = value,
        });

        Assert.Equal(path, editedPath);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string WritePath(string name) => Path.Combine(_tempDir, name);

    private string Write(string name, string content, Encoding enc)
    {
        string path = WritePath(name);
        File.WriteAllText(path, content, enc);
        return path;
    }

    private string WriteLargeTextFile(string name, long minBytes, string tailLine)
    {
        string path = WritePath(name);
        byte[] line = Encoding.UTF8.GetBytes(new string('a', 1024) + "\n");
        byte[] tail = Encoding.UTF8.GetBytes(tailLine + "\n");

        using var stream = File.Create(path);
        while (stream.Length < minBytes)
            stream.Write(line);
        stream.Write(tail);
        return path;
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        new(keyChar, key, shift, alt, control);

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent MouseWheelDown() =>
        new(0, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None);

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(Key((ConsoleKey)char.ToUpperInvariant(ch), ch));
    }

    private static string WrittenText(FakeConsoleDriver driver) =>
        string.Concat(driver.WriteRecords.Select(record => record.Text));

    private static string ComposeRow(FakeConsoleDriver driver, int y, int width)
    {
        var row = Enumerable.Repeat(' ', width).ToArray();
        foreach (var record in driver.WriteRecords.Where(record => record.Y == y))
        {
            for (int i = 0; i < record.Text.Length && record.X + i < width; i++)
                row[record.X + i] = record.Text[i];
        }

        return new string(row);
    }

    private sealed class FakeLocalFileMonitorFactory : ILocalFileMonitorFactory
    {
        private readonly bool _notificationsAvailable;

        public FakeLocalFileMonitorFactory(bool notificationsAvailable = true)
        {
            _notificationsAvailable = notificationsAvailable;
        }

        public int CreateCount { get; private set; }

        public List<FakeLocalFileMonitor> Monitors { get; } = [];

        public FakeLocalFileMonitor Current => Monitors[^1];

        public ILocalFileMonitor Create(string filePath)
        {
            _ = filePath;
            CreateCount++;
            var monitor = new FakeLocalFileMonitor(_notificationsAvailable);
            Monitors.Add(monitor);
            return monitor;
        }
    }

    private sealed class FakeLocalFileMonitor : ILocalFileMonitor
    {
        private long _changeVersion;

        public FakeLocalFileMonitor(bool notificationsAvailable)
        {
            NotificationsAvailable = notificationsAvailable;
        }

        public long ChangeVersion => _changeVersion;

        public bool NotificationsAvailable { get; private set; }

        public bool IsDisposed { get; private set; }

        public void MarkDirty() => _changeVersion++;

        public void FailNotifications()
        {
            NotificationsAvailable = false;
            MarkDirty();
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeTextClipboard : ITextClipboard
    {
        public string? Text { get; private set; }

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }

        public bool TryGetText(out string text)
        {
            text = Text ?? string.Empty;
            return Text is not null;
        }
    }
}
