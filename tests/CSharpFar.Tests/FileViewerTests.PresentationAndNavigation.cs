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

}
