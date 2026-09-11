using System.Reflection;
using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Testing;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ViewerPresentationIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public ViewerPresentationIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarViewerPresentation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Show_TxtTableLikeContentStaysRawInAuto()
    {
        string path = Write("table.txt", "| A | B |\n| --- | --- |\n| C | D |\n");
        var driver = ViewerDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string content = Content(driver);
        Assert.Contains("| A | B |", content);
        Assert.DoesNotContain("│ A", content);
    }

    [Fact]
    public void Show_MarkdownUsesAutoPresentationAndFunctionBarOffersRaw()
    {
        string path = Write("table.md", "| A | B |\n| --- | --- |\n| C | D |\n");
        var driver = ViewerDriver(width: 100);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string content = Content(driver, width: 100);
        Assert.Contains("│ A │ B │", content);
        Assert.Contains("├", content);
        Assert.Contains("5Raw", driver.GetRegionText(new Rect(0, 9, 100, 1)));
    }

    [Fact]
    public void Show_F5SwitchesToRawWithoutChangingSourcePosition()
    {
        string path = Write("raw-position.md", "| A | B |\n| --- | --- |\n| C | D |\n");
        var driver = ViewerDriver(width: 100);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string top = driver.GetRegionText(new Rect(0, 1, 100, 1));
        Assert.Contains("| --- | --- |", top);
        Assert.DoesNotContain("├", top);
        Assert.Contains("5Auto", driver.GetRegionText(new Rect(0, 9, 100, 1)));
    }

    [Fact]
    public void Show_PresentationModeSurvivesSiblingNavigation()
    {
        string first = Write("first.md", "| First | X |\n| --- | --- |\n| one | 1 |\n");
        string second = Write("second.md", "| Second | X |\n| --- | --- |\n| two | 2 |\n");
        var driver = ViewerDriver();
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Add, '+'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        string? changed = null;

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(first, new LargeFileViewerOptions
        {
            FilePaths = [first, second],
            CurrentFileIndex = 0,
            CurrentFileChanged = path => changed = path,
        });

        Assert.Equal(second, changed);
        string content = Content(driver);
        Assert.Contains("| Second | X |", content);
        Assert.DoesNotContain("│ Second", content);
    }

    [Fact]
    public void Show_HexModeIsUnaffectedByPresentationMode()
    {
        string path = Write("hex.md", "ABC\n");
        var driver = ViewerDriver();
        driver.EnqueueKey(Key(ConsoleKey.F4));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string screen = driver.GetRegionText(new Rect(0, 0, 80, 9));
        Assert.Contains("HEX", screen);
        Assert.Contains("00000000", screen);
        Assert.Contains("41 42 43", screen);
    }

    [Fact]
    public void Session_SingleWrappedHeaderUsesBoundedLookAheadToFindSeparator()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("| A | B |\n| --- | --- |\n| C | D |\n");
        var reader = new MemoryFileByteReader(bytes);
        var cache = new BlockCache(reader, blockSize: 32, capacity: 8);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        ScannedLine header = scanner.ReadLinesAsync(0, 1, 256).GetAwaiter().GetResult().Lines.Single();
        var session = new ViewerPresentationSession();

        PresentedLine result = session.Present(
            ViewerPresentationMode.Auto,
            "virtual://docs/table.md",
            scanner,
            [header],
            80).Single();

        Assert.Equal("│ A │ B │", result.Text);
    }

    [Fact]
    public void Show_WrappingRemainsFunctionalWithPresentationEnabled()
    {
        string path = Write(
            "wrapped.md",
            "| Column | Value |\n| --- | --- |\n| very-long-value-for-wrapping | data |\n");
        var driver = ViewerDriver(width: 30);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string screen = driver.GetRegionText(new Rect(0, 0, 30, 9));
        Assert.Contains("WRAP-W", screen);
        Assert.Contains("│", Content(driver, width: 30));
    }

    [Fact]
    public void Show_SearchHighlightsMappedTextInsidePresentedCell()
    {
        string path = Write(
            "search.md",
            "| Name | Description |\n| --- | --- |\n| Row | target-value |\n");
        var driver = ViewerDriver(width: 60, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "target");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 60, 1));
        int targetX = row.IndexOf("target", StringComparison.Ordinal);
        Assert.True(targetX > 0, row);
        Assert.Equal('t', driver.GetCell(targetX, 1).Character);
        Assert.True(
            driver.GetCell(targetX, 1).Foreground != driver.GetCell(0, 1).Foreground ||
            driver.GetCell(targetX, 1).Background != driver.GetCell(0, 1).Background);
    }

    [Fact]
    public void ViewerSearchEngine_DoesNotSearchPresentationDecoration()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("| A | B |\n| --- | --- |\n| C | D |\n");
        var reader = new MemoryFileByteReader(bytes);
        var cache = new BlockCache(reader);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        var state = new LargeFileViewerState(cache, scanner);

        ViewerSearchMatch? match = ViewerSearchEngine.Find(
            reader,
            state,
            new ViewerSearchRequest("│", CaseSensitive: true, WholeWords: false, UseRegex: false, SearchHex: false),
            searchBackward: false);

        Assert.Null(match);
    }

    [Fact]
    public void Show_HorizontalScrollingWorksOnPresentedWideTable()
    {
        string path = Write(
            "wide.md",
            "| 0123456789ABCDEFGHIJ | B |\n| --- | --- |\n| 0123456789abcdefghij | C |\n");
        var driver = ViewerDriver(width: 16);
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string top = driver.GetRegionText(new Rect(0, 1, 16, 1));
        Assert.DoesNotContain("│ 012", top);
        Assert.Contains("456789", top);
    }

    [Fact]
    public void Show_VirtualMarkdownSourceUsesSamePresentationPath()
    {
        var reader = new MemoryFileByteReader(
            Encoding.UTF8.GetBytes("| A | B |\n| --- | --- |\n| C | D |\n"));
        var driver = ViewerDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver))
            .Show("virtual://archive/readme.md?rev=42", reader);

        Assert.Contains("│ A │ B │", Content(driver));
    }

    [Fact]
    public void Session_DistantTableJumpUsesBoundedReadsAndFallsBackRaw()
    {
        const int targetRow = 5000;
        var source = new StringBuilder();
        source.Append("| Header | Value |\n| --- | --- |\n");
        long targetOffset = -1;
        string targetText = string.Empty;
        for (int i = 0; i < 10000; i++)
        {
            string line = $"| row-{i:D5}-{new string('x', 100)} | {i:D5} |";
            if (i == targetRow)
            {
                targetOffset = Encoding.UTF8.GetByteCount(source.ToString());
                targetText = line;
            }
            source.Append(line).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(source.ToString());
        var reader = new RecordingFileByteReader(bytes);
        var cache = new BlockCache(reader, blockSize: 4096, capacity: 128);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        reader.ResetMetrics();
        int targetByteLength = Encoding.UTF8.GetByteCount(targetText) + 1;
        var visible = new ScannedLine(targetOffset, targetOffset + targetByteLength, targetText);
        var session = new ViewerPresentationSession();

        PresentedLine result = session.Present(
            ViewerPresentationMode.Auto,
            "virtual://large/table.md",
            scanner,
            [visible],
            80).Single();

        Assert.Equal(targetText, result.Text);
        Assert.True(bytes.Length > 1_000_000);
        Assert.True(reader.TotalBytesRead < 512 * 1024, $"Read {reader.TotalBytesRead} of {bytes.Length} bytes");
        Assert.True(reader.ReadCalls < 150, $"Read calls: {reader.ReadCalls}");
    }

    [Fact]
    public void ResetScannerInvalidatesPresentationLayoutCache()
    {
        var firstReader = new MemoryFileByteReader(
            Encoding.UTF8.GetBytes("| A | B |\n| --- | --- |\n| C | D |\n"));
        var firstCache = new BlockCache(firstReader, blockSize: 64, capacity: 8);
        var firstScanner = LineScanner.CreateAsync(firstCache, firstReader).GetAwaiter().GetResult();
        var state = new LargeFileViewerState(firstCache, firstScanner);
        IReadOnlyList<ScannedLine> firstLines = firstScanner.ReadLinesAsync(0, 3, 256).GetAwaiter().GetResult().Lines;
        Assert.StartsWith("│", state.Presentation.Present(
            ViewerPresentationMode.Auto, "cache.md", firstScanner, firstLines, 80)[2].Text);

        var secondReader = new MemoryFileByteReader(Encoding.UTF8.GetBytes("| C | D |\n"));
        var secondCache = new BlockCache(secondReader, blockSize: 64, capacity: 8);
        var secondScanner = LineScanner.CreateAsync(secondCache, secondReader).GetAwaiter().GetResult();
        state.ResetScanner(secondScanner, secondScanner.Detection.Selection);
        ScannedLine secondLine = secondScanner.ReadLinesAsync(0, 1, 256).GetAwaiter().GetResult().Lines.Single();

        PresentedLine result = state.Presentation.Present(
            ViewerPresentationMode.Auto, "cache.md", secondScanner, [secondLine], 80).Single();

        Assert.Equal(secondLine.Text, result.Text);
    }

    [Fact]
    public void Session_ProviderFailureFallsBackRawAndDoesNotLoop()
    {
        var reader = new MemoryFileByteReader(Encoding.UTF8.GetBytes("plain\n"));
        var cache = new BlockCache(reader, blockSize: 32, capacity: 4);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        ScannedLine source = scanner.ReadLinesAsync(0, 1, 256).GetAwaiter().GetResult().Lines.Single();
        var session = new ViewerPresentationSession();
        var provider = new ThrowingProvider();
        SetPrivate(session, "_sourcePath", "broken.md");
        SetPrivate(session, "_provider", provider);

        PresentedLine first = session.Present(
            ViewerPresentationMode.Auto, "broken.md", scanner, [source], 80).Single();
        PresentedLine second = session.Present(
            ViewerPresentationMode.Auto, "broken.md", scanner, [source], 80).Single();

        Assert.Equal(source.Text, first.Text);
        Assert.Equal(source.Text, second.Text);
        Assert.Equal(1, provider.Calls);
    }

    private static FakeConsoleDriver ViewerDriver(int width = 80, int height = 10) =>
        new(width, height);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string Content(FakeConsoleDriver driver, int width = 80, int height = 10) =>
        driver.GetRegionText(new Rect(0, 1, width, height - 2));

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        new(keyChar, key, shift, alt, control);

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(Key((ConsoleKey)char.ToUpperInvariant(ch), ch));
    }

    private static void SetPrivate(object target, string fieldName, object? value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} not found.");
        field.SetValue(target, value);
    }

    private sealed class ThrowingProvider : IViewerPresentationProvider
    {
        public int Calls { get; private set; }

        public IReadOnlyList<PresentedLine> Present(ViewerPresentationContext context)
        {
            Calls++;
            throw new InvalidOperationException("test provider failure");
        }

        public void Reset()
        {
        }
    }

    private sealed class RecordingFileByteReader(byte[] content) : IFileByteReader
    {
        public long Length => content.LongLength;
        public int ReadCalls { get; private set; }
        public long TotalBytesRead { get; private set; }

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            if (offset < 0 || offset >= content.LongLength)
                return Task.FromResult(0);

            int count = (int)Math.Min(buffer.Length, content.LongLength - offset);
            content.AsMemory((int)offset, count).CopyTo(buffer);
            TotalBytesRead += count;
            return Task.FromResult(count);
        }

        public void ResetMetrics()
        {
            ReadCalls = 0;
            TotalBytesRead = 0;
        }
    }
}
