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
public partial class FileViewerTests
{
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

        long streamedCharacters = 0;
        int largestChunk = 0;
        await scanner.VisitLineTextChunksAsync(line, chunk =>
        {
            streamedCharacters += chunk.Length;
            largestChunk = Math.Max(largestChunk, chunk.Length);
        });

        Assert.Equal(content.LongLength, streamedCharacters);
        Assert.InRange(largestChunk, 1, 8192);
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

}
