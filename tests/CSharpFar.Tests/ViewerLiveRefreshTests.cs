using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests;

public sealed class ViewerLiveRefreshTests : IDisposable
{
    private readonly string _tempDir;

    public ViewerLiveRefreshTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarViewerLive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RandomAccessReader_DoesNotHoldLocalFileBetweenReads()
    {
        string path = Write("reader.txt", "original");
        using var reader = new RandomAccessFileByteReader(path);

        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        string renamed = Path.Combine(_tempDir, "renamed.txt");
        File.Move(path, renamed);
        File.Move(renamed, path);
        File.Delete(path);
        File.WriteAllText(path, "replaced", new UTF8Encoding(false));

        var buffer = new byte[8];
        int read = await reader.ReadAsync(0, buffer);

        Assert.Equal(8, read);
        Assert.Equal("replaced", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void LocalMonitor_DetectsSameLengthOverwrite()
    {
        string path = Write("overwrite.txt", "AAAAAA");
        using var monitor = new LocalFileChangeMonitor(path);

        File.WriteAllText(path, "BBBBBB", new UTF8Encoding(false));

        LocalFileChange change = WaitForChange(monitor);
        Assert.Equal(LocalFileChangeKind.Reload, change.Kind);
    }

    [Fact]
    public void LocalMonitor_DetectsAppend()
    {
        string path = Write("append.txt", "first\n");
        using var monitor = new LocalFileChangeMonitor(path);

        File.AppendAllText(path, "second\n", new UTF8Encoding(false));

        LocalFileChange change = WaitForChange(monitor);
        Assert.Contains(change.Kind, new[] { LocalFileChangeKind.Append, LocalFileChangeKind.Reload });
        Assert.True(change.Current.Length > change.Previous.Length);
    }

    [Fact]
    public void LocalMonitor_DetectsTruncate()
    {
        string path = Write("truncate.txt", "long-value");
        using var monitor = new LocalFileChangeMonitor(path);

        File.WriteAllText(path, "x", new UTF8Encoding(false));

        LocalFileChange change = WaitForChange(monitor);
        Assert.Equal(LocalFileChangeKind.Reload, change.Kind);
        Assert.True(change.Current.Length < change.Previous.Length);
    }

    [Fact]
    public void LocalMonitor_DetectsDeleteAndRecreate()
    {
        string path = Write("recreate.txt", "old");
        using var monitor = new LocalFileChangeMonitor(path);

        File.Delete(path);
        LocalFileChange deleted = WaitForChange(monitor);
        Assert.Equal(LocalFileChangeKind.Missing, deleted.Kind);
        monitor.Accept(deleted);

        File.WriteAllText(path, "new", new UTF8Encoding(false));
        LocalFileChange recreated = WaitForChange(monitor);
        Assert.Equal(LocalFileChangeKind.Reload, recreated.Kind);
    }

    [Fact]
    public void LocalMonitor_DetectsAtomicReplace()
    {
        string path = Write("replace.txt", "old-value");
        string replacement = Write("replace.new", "new-value");
        using var monitor = new LocalFileChangeMonitor(path);

        File.Move(replacement, path, overwrite: true);

        LocalFileChange change = WaitForChange(monitor);
        Assert.Equal(LocalFileChangeKind.Reload, change.Kind);
    }

    [Fact]
    public void Show_IdleLocalViewerAllowsExclusiveOpen()
    {
        string path = Write("idle-lock.txt", "content");
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        bool openedExclusively = false;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount != 1 || openedExclusively)
                return;

            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            openedExclusively = true;
        });
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.True(openedExclusively);
    }

    [Fact]
    public void Show_FCyclesOffWatchTailOff()
    {
        string path = WriteLines("live-cycle.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        string? finalHeader = null;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount == 1)
                finalHeader = d.GetRegionText(new Rect(0, 0, 60, 1));
        });
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string writes = WrittenText(driver);
        Assert.Contains("WATCH", writes);
        Assert.Contains("TAIL", writes);
        Assert.DoesNotContain("WATCH", finalHeader);
        Assert.DoesNotContain("TAIL", finalHeader);
    }

    [Fact]
    public void Show_TailBecomesWatchWhenMovingUp()
    {
        string path = WriteLines("tail-up.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.UpArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("WATCH", WrittenText(driver));
    }

    [Fact]
    public void Show_MovingDownToLastViewportEnablesTail()
    {
        string path = WriteLines("down-tail.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        for (int i = 0; i < 4; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_PageDownCannotOverscrollTextPastLastPage()
    {
        string path = WriteLines("text-eof.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        string? content = null;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount == 1)
                content = d.GetRegionText(new Rect(0, 1, 60, 4));
        });
        for (int i = 0; i < 4; i++)
            driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(content);
        Assert.Contains("line05", content);
        Assert.Contains("line08", content);
        Assert.EndsWith("line08".PadRight(60), content);
    }

    [Fact]
    public void Show_PageDownCannotOverscrollHexPastLastRow()
    {
        string path = Path.Combine(_tempDir, "hex-eof.bin");
        File.WriteAllBytes(path, Enumerable.Range(0, 70).Select(i => (byte)i).ToArray());
        var driver = new FakeConsoleDriver(width: 80, height: 6);
        string? content = null;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount == 1)
                content = d.GetRegionText(new Rect(0, 1, 80, 4));
        });
        for (int i = 0; i < 4; i++)
            driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(content);
        Assert.Contains("00000010", content);
        Assert.Contains("00000040", content);
        Assert.DoesNotContain("00000050", content);
    }

    [Fact]
    public void Show_WrappedEndShowsTrueVisualTail()
    {
        string path = Write(
            "wrapped-tail.txt",
            string.Concat(Enumerable.Range(0, 8).Select(i => $"{i}{new string((char)('a' + i), 9)}")));
        var driver = new FakeConsoleDriver(width: 10, height: 6);
        string? content = null;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount == 1)
                content = d.GetRegionText(new Rect(0, 1, 10, 4));
        });
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(content);
        Assert.Contains("7hhhhhhhhh", content);
        Assert.DoesNotContain("0aaaaaaaaa", content);
    }

    [Fact]
    public void Show_FastPageAndWheelCannotCreateEmptyPageAfterEof()
    {
        string path = WriteLines("fast-eof.txt", 12);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        string? content = null;
        OnTryRead(driver, d =>
        {
            if (d.PendingInputCount == 1)
                content = d.GetRegionText(new Rect(0, 1, 60, 4));
        });
        driver.EnqueueKey(Key(ConsoleKey.PageDown, alt: true));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            0, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            0, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(content);
        Assert.Contains("line12", content);
        Assert.EndsWith("line12".PadRight(60), content);
    }

    [Fact]
    public void Show_WatchRefreshesSameLengthOverwriteAndKeepsAnchor()
    {
        string path = WriteLines("watch-overwrite.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
            {
                string text = File.ReadAllText(path, Encoding.UTF8)
                    .Replace("line02", "LINE02", StringComparison.Ordinal);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            else if (reads == 2)
            {
                d.EnqueueKey(Key(ConsoleKey.F10));
            }
        });
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("LINE02", WrittenText(driver));
        Assert.Contains("WATCH", WrittenText(driver));
    }

    [Fact]
    public void Show_WatchAppendRefreshesWithoutMovingViewport()
    {
        string path = WriteLines("watch-append.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        string? contentAfterRefresh = null;
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
            {
                File.AppendAllText(path, "line09\n", new UTF8Encoding(false));
            }
            else if (reads == 2)
            {
                contentAfterRefresh = d.GetRegionText(new Rect(0, 1, 60, 4));
                d.EnqueueKey(Key(ConsoleKey.F10));
            }
        });
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(contentAfterRefresh);
        Assert.StartsWith("line02", contentAfterRefresh);
        Assert.DoesNotContain("line09", contentAfterRefresh);
        Assert.Contains("WATCH", WrittenText(driver));
    }

    [Fact]
    public void Show_TailAppendMovesToNewLastPage()
    {
        string path = WriteLines("tail-append.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
                File.AppendAllText(path, "line09\n", new UTF8Encoding(false));
            else if (reads == 2)
                d.EnqueueKey(Key(ConsoleKey.F10));
        });
        driver.EnqueueKey(Key(ConsoleKey.End));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("line09", WrittenText(driver));
        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void Show_TemporaryExclusiveLockKeepsViewerAliveUntilRetrySucceeds()
    {
        string path = WriteLines("exclusive-retry.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        FileStream? exclusive = null;
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
            {
                string text = File.ReadAllText(path, Encoding.UTF8)
                    .Replace("line01", "LINE01", StringComparison.Ordinal);
                File.WriteAllText(path, text, new UTF8Encoding(false));
                exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else if (reads == 2)
            {
                exclusive!.Dispose();
                exclusive = null;
            }
            else if (reads == 3)
            {
                d.EnqueueKey(Key(ConsoleKey.F10));
            }
        });
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));

        try
        {
            FileViewerFor(new ScreenRenderer(driver)).Show(path);
        }
        finally
        {
            exclusive?.Dispose();
        }

        Assert.Contains("LINE01", WrittenText(driver));
    }

    [Fact]
    public void Show_TailSurvivesDeleteAndRecreate()
    {
        string path = WriteLines("tail-recreate.txt", 8);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
                File.Delete(path);
            else if (reads == 2)
                File.WriteAllText(path, "new01\nnew02\nnew03\n", new UTF8Encoding(false));
            else if (reads == 3)
                d.EnqueueKey(Key(ConsoleKey.F10));
        });
        driver.EnqueueKey(Key(ConsoleKey.End));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.Contains("new03", WrittenText(driver));
    }

    [Fact]
    public void Show_ResizeNormalizesTailToNewLastViewport()
    {
        string path = WriteLines("resize-tail.txt", 10);
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        string? resizedContent = null;
        OnRead(driver, (reads, d) =>
        {
            if (reads == 1)
                d.SetSize(60, 10);
            else if (reads == 2)
            {
                resizedContent = d.GetRegionText(new Rect(0, 1, 60, 8));
                d.EnqueueKey(Key(ConsoleKey.F10));
            }
        });
        driver.EnqueueKey(Key(ConsoleKey.End));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        Assert.NotNull(resizedContent);
        Assert.Contains("line03", resizedContent);
        Assert.Contains("line10", resizedContent);
    }

    [Fact]
    public void Show_VirtualSnapshotReaderKeepsExistingBehavior()
    {
        var reader = new MemoryFileByteReader(Encoding.UTF8.GetBytes("virtual-content"));
        var driver = new FakeConsoleDriver(width: 60, height: 6);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show("virtual://snapshot.txt", reader);

        Assert.Contains("virtual-content", WrittenText(driver));
    }

    private static void OnTryRead(
        FakeConsoleDriver driver,
        Action<FakeConsoleDriver> callback)
    {
        void Arm()
        {
            driver.BeforeTryReadInput = current =>
            {
                callback(current);
                Arm();
            };
        }

        Arm();
    }

    private static void OnRead(
        FakeConsoleDriver driver,
        Action<int, FakeConsoleDriver> callback)
    {
        int count = 0;

        void Arm()
        {
            driver.BeforeReadInput = current =>
            {
                count++;
                callback(count, current);
                Arm();
            };
        }

        Arm();
    }

    private LocalFileChange WaitForChange(LocalFileChangeMonitor monitor)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < timeout)
        {
            LocalFileChange change = monitor.Check();
            if (change.Kind is not (LocalFileChangeKind.None or LocalFileChangeKind.Unavailable))
                return change;

            Thread.Sleep(10);
        }

        return monitor.Check();
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string WriteLines(string name, int count) =>
        Write(name, string.Concat(Enumerable.Range(1, count).Select(i => $"line{i:00}\n")));

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        new(keyChar, key, shift, alt, control);

    private static string WrittenText(FakeConsoleDriver driver) =>
        string.Concat(driver.WriteRecords.Select(record => record.Text));
}
