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

}
