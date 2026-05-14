using System.Text;
using CSharpFar.Console;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Text;
using CSharpFar.Tests.Fakes;

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

    [Fact]
    public void ReadLines_ReturnsEmptyForEmptyFile()
    {
        string path = Write("empty.txt", "", Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Empty(lines);
    }

    [Fact]
    public void ReadLines_PreservesLineCount()
    {
        string content = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}"));
        string path = Write("ten.txt", content, Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Equal(10, lines.Length);
    }

    // -- Unified file viewer --------------------------------------------------

    [Fact]
    public void Show_SmallTextFileUsesUnifiedStreamingViewer()
    {
        string path = Write("small-viewer.txt", "first\nsecond", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT", writes);
        Assert.Contains("first", writes);
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

        new FileViewer(screen).Show(path);

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

        new FileViewer(screen).Show(path);

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

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("HEX", writes);
        Assert.Contains("00000000", writes);
    }

    [Fact]
    public void Show_TextFileCanToggleToHexMode()
    {
        string path = Write("toggle-text.txt", "ABC", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.H, 'h'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("HEX", writes);
        Assert.Contains("41 42 43", writes);
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

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT", writes);
        Assert.Contains("A B", writes);
    }

    [Fact]
    public void Show_TextModeSanitizesControlCharacters()
    {
        string path = Write("controls.txt", "A\u001B[31mB\u0007C", new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.H, 'h'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("A [31mB C", writes);
    }

    [Fact]
    public void SanitizeTextForConsole_ReplacesControlCharacters()
    {
        string sanitized = LargeFileViewer.SanitizeTextForConsole("A\u001B[31mB\u0007C\0D\tE");

        Assert.Equal("A [31mB C D    E", sanitized);
    }

    [Fact]
    public void Show_FollowKeyShowsFollowStatus()
    {
        string path = WriteLargeTextFile(
            "large-follow.txt",
            LargeTestFileSize,
            "tail-follow-file");
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

        Assert.Contains(" F ", WrittenText(driver));
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
        driver.EnqueueKey(Key(ConsoleKey.F8));
        for (int i = 0; i < 5; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

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
        driver.EnqueueKey(Key(ConsoleKey.F8));
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

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
        driver.EnqueueKey(Key(ConsoleKey.F8));
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var screen = new ScreenRenderer(driver);

        new FileViewer(screen).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("TEXT CP866", writes);
        Assert.Contains("A B", writes);
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

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar = '\0') =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private static string WrittenText(FakeConsoleDriver driver) =>
        string.Concat(driver.WriteRecords.Select(record => record.Text));
}
