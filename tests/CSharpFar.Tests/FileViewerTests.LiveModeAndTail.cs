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
    public void Show_InitialOffDoesNotStartLiveMonitor()
    {
        string path = Write("live-off.txt", "content", new UTF8Encoding(false));
        var factory = new FakeLocalFileMonitorFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver), factory).Show(path);

        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void Show_OffToWatchForcesResyncWithoutPriorNotification()
    {
        string path = Write("live-initial-resync.txt", "AAAA", new UTF8Encoding(false));
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);
        var factory = new FakeLocalFileMonitorFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        int readCount = 0;
        driver.BeforeTryReadInput = _ =>
        {
            if (readCount++ != 0)
                return;

            File.WriteAllText(path, "BBBB", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, originalWriteTime);
        };
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver), factory).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("BBBB", content);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void Show_WatchToTailRefreshesAppendWithoutTiming()
    {
        string path = Write("live-tail-append.txt", "first", new UTF8Encoding(false));
        var factory = new FakeLocalFileMonitorFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        int readCount = 0;
        Action<FakeConsoleDriver>? beforeInput = null;
        beforeInput = current =>
        {
            if (readCount++ == 0)
            {
                current.BeforeTryReadInput = beforeInput;
                return;
            }

            File.AppendAllText(path, "\nAPPENDED", new UTF8Encoding(false));
            factory.Current.MarkDirty();
        };
        driver.BeforeTryReadInput = beforeInput;
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver), factory).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("APPENDED", content);
        Assert.Equal(1, factory.CreateCount);
        Assert.True(factory.Current.IsDisposed);
    }

    [Fact]
    public void Show_WatchRefreshesSameLengthOverwriteFromDeterministicSignal()
    {
        string path = Write("live-same-length.txt", "AAAA", new UTF8Encoding(false));
        var factory = new FakeLocalFileMonitorFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        int readCount = 0;
        Action<FakeConsoleDriver>? beforeInput = null;
        beforeInput = current =>
        {
            if (readCount++ == 0)
            {
                current.BeforeTryReadInput = beforeInput;
                return;
            }

            File.WriteAllText(path, "BBBB", new UTF8Encoding(false));
            factory.Current.MarkDirty();
        };
        driver.BeforeTryReadInput = beforeInput;
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F, 'f'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver), factory).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("BBBB", content);
        Assert.Equal(1, factory.CreateCount);
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
    }

    [Fact]
    public void Show_WrappedEndShowsTrueTailPastFormerFourMiBLimit()
    {
        const int oldScanLimit = 4 * 1024 * 1024;
        string path = WritePath("wrapped-tail-over-four-mib.txt");
        File.WriteAllText(
            path,
            new string('x', oldScanLimit + 128) + "REAL-END",
            new UTF8Encoding(false));
        var driver = new FakeConsoleDriver(width: 512, height: 7);
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string content = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 512, 5));
        Assert.Contains("REAL-END", content);
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
    public void Show_TransientReadDuringOffNavigationPreservesViewportAndCanRetry()
    {
        string path = WritePath("transient-off-navigation.txt");
        string middle = string.Join(
            '\n',
            Enumerable.Range(0, 40000).Select(i => $"line{i:D5}"));
        File.WriteAllText(
            path,
            $"START\n{middle}\nTRANSIENT-TAIL",
            new UTF8Encoding(false));

        var monitorFactory = new FakeLocalFileMonitorFactory();
        var readerFactory = new ControlledLocalFileByteReaderFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        string? afterFailure = null;
        string? afterRecovery = null;
        int hook = 0;
        Action<FakeConsoleDriver>? beforeInput = null;
        beforeInput = current =>
        {
            switch (hook++)
            {
                case 0:
                    readerFactory.Current.FailNextLength = true;
                    current.BeforeTryReadInput = beforeInput;
                    break;
                case 1:
                    afterFailure = current.GetRegionText(
                        new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
                    current.BeforeTryReadInput = beforeInput;
                    break;
                case 2:
                    afterRecovery = current.GetRegionText(
                        new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
                    break;
            }
        };
        driver.BeforeTryReadInput = beforeInput;
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(
            new ScreenRenderer(driver),
            monitorFactory,
            readerFactory).Show(path);

        Assert.NotNull(afterFailure);
        Assert.Contains("START", afterFailure);
        Assert.DoesNotContain("TRANSIENT-TAIL", afterFailure);
        Assert.NotNull(afterRecovery);
        Assert.Contains("TRANSIENT-TAIL", afterRecovery);
    }

}
