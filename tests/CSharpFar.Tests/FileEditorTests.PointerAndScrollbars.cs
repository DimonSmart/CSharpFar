using CSharpFar.App.Editor;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class FileEditorTests
{
    [Fact]
    public void Show_TextMouseClickMovesCursor()
    {
        string filePath = Path.Combine(_tempDir, "editor-mouse-click.txt");
        File.WriteAllText(filePath, "abc\ndef");

        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(LeftMouse(x: 1, y: 2));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("abc\ndXef", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_TextMouseDoubleClickSelectsWord()
    {
        string filePath = Path.Combine(_tempDir, "editor-mouse-word.txt");
        File.WriteAllText(filePath, "alpha beta gamma");

        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(LeftMouseDoubleClick(x: 7, y: 1));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("alpha X gamma", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_TextMouseDragSelectsRange()
    {
        string filePath = Path.Combine(_tempDir, "editor-mouse-drag.txt");
        File.WriteAllText(filePath, "abcdef");

        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(LeftMouse(x: 1, y: 1));
        driver.EnqueueInput(MouseMove(x: 4, y: 1));
        driver.EnqueueInput(MouseUp(x: 4, y: 1));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("aXef", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_TextMouseDragContinuesOutsideContentThroughCapture()
    {
        string filePath = Path.Combine(_tempDir, "editor-mouse-captured-drag.txt");
        File.WriteAllText(filePath, "abcdef");

        var driver = new FakeConsoleDriver(width: 8, height: 6);
        driver.EnqueueInput(LeftMouse(x: 1, y: 1));
        driver.EnqueueInput(MouseMove(x: 40, y: 1));
        driver.EnqueueInput(MouseUp(x: 40, y: 1));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("aX", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ResizeBeforeMouseInputUsesCommittedNewContentGeometry()
    {
        string filePath = Path.Combine(_tempDir, "editor-resize-mouse.txt");
        File.WriteAllText(filePath, "one\ntwo\nthree\nfour\nfive");

        var driver = new FakeConsoleDriver(width: 80, height: 5);
        driver.BeforeTryReadInput = current => current.SetSize(80, 8);
        driver.EnqueueInput(LeftMouse(x: 0, y: 4));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\ntwo\nthree\nXfour\nfive", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_NonScrollableDocumentRepaintsReservedScrollbarGutter()
    {
        string filePath = Path.Combine(_tempDir, "editor-non-scrollable-gutter.txt");
        File.WriteAllText(filePath, "short");

        var driver = new FakeConsoleDriver(width: 20, height: 8);
        var renderer = new ScreenRenderer(driver);
        using (renderer.BeginFrame())
        {
            for (int y = 1; y <= 5; y++)
                renderer.WriteChar(19, y, '#', CellStyle.Default);
        }

        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(renderer, filePath);

        for (int y = 1; y <= 5; y++)
            Assert.Equal(' ', driver.GetRow(y)[19]);
    }

    [Fact]
    public void Show_ScrollbarIncreaseButtonScrollsTextDown()
    {
        string filePath = Path.Combine(_tempDir, "editor-scrollbar-increase.txt");
        File.WriteAllText(filePath, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")));

        var driver = new FakeConsoleDriver(width: 80, height: 8);
        ClearRecordedOperationsBeforeFirstInput(driver);
        driver.EnqueueInput(LeftMouse(x: 79, y: 5));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 4 &&
            record.Y == 1 &&
            record.Text == "2");
    }

    [Fact]
    public void Show_ScrollbarThumbDragScrollsText()
    {
        string filePath = Path.Combine(_tempDir, "editor-scrollbar-drag.txt");
        File.WriteAllText(filePath, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")));

        var driver = new FakeConsoleDriver(width: 80, height: 10);
        ClearRecordedOperationsBeforeFirstInput(driver);
        driver.EnqueueInput(LeftMouse(x: 79, y: 2));
        driver.EnqueueInput(MouseMove(x: 79, y: 6));
        driver.EnqueueInput(MouseUp(x: 79, y: 6));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 4 &&
            record.Y == 1 &&
            record.Text == "6");
    }

}
