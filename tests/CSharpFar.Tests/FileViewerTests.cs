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
public partial class FileViewerTests : IDisposable
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

    private sealed class ControlledLocalFileByteReaderFactory : ILocalFileByteReaderFactory
    {
        public ControlledLocalFileByteReader Current { get; private set; } = null!;

        public ILocalFileByteReader Create(string filePath)
        {
            Current = new ControlledLocalFileByteReader(filePath);
            return Current;
        }
    }

    private sealed class ControlledLocalFileByteReader : ILocalFileByteReader
    {
        private readonly RandomAccessFileByteReader _inner;
        private int _injectedFailureVersion;

        public ControlledLocalFileByteReader(string filePath)
        {
            _inner = new RandomAccessFileByteReader(filePath);
        }

        public bool FailNextLength { get; set; }

        public int TransientFailureVersion =>
            _inner.TransientFailureVersion + _injectedFailureVersion;

        public long Length
        {
            get
            {
                long length = _inner.Length;
                if (!FailNextLength)
                    return length;

                FailNextLength = false;
                _injectedFailureVersion++;
                return length;
            }
        }

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(offset, buffer, cancellationToken);

        public void Dispose() => _inner.Dispose();
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
