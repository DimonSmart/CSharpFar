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
    public void Show_F8DeletesSelection()
    {
        string filePath = Path.Combine(_tempDir, "f8-selection-delete.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("bc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_F8WithoutSelectionDeletesCurrentLine()
    {
        string filePath = Path.Combine(_tempDir, "f8-line-delete.txt");
        File.WriteAllText(filePath, "one\ntwo\nthree");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\nthree", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_CtrlKDeletesToLineEnd()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-k-delete-tail.txt");
        File.WriteAllText(filePath, "alpha beta");

        var driver = new FakeConsoleDriver(80, 25);
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\v', ConsoleKey.K, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("alpha ", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlYDeletesCurrentLine()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-y-delete-line.txt");
        File.WriteAllText(filePath, "one\ntwo\nthree");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0019', ConsoleKey.Y, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\nthree", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_CtrlShiftZRedoesLastUndo()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-shift-z-redo.txt");
        File.WriteAllText(filePath, "a");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u001a', ConsoleKey.Z, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u001a', ConsoleKey.Z, shift: true, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("Xa", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlPCopiesPersistentSelectionToCursor()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-p-copy-block.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0010', ConsoleKey.P, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("abca", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlDDeletesSelection()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-d-delete.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0004', ConsoleKey.D, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("bc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_AltUAndAltIShiftSelectedLines()
    {
        string filePath = Path.Combine(_tempDir, "alt-u-i-shift.txt");
        File.WriteAllText(filePath, " abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.U, shift: false, alt: true, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.I, shift: false, alt: true, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(" abc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_NumberedBookmarkSetAndGoReturnsCursor()
    {
        string filePath = Path.Combine(_tempDir, "numbered-bookmark.txt");
        File.WriteAllText(filePath, "one\ntwo");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.D2, shift: true, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.D2, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\nXtwo", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_ShiftEnterInsertsActivePanelFileName()
    {
        string filePath = Path.Combine(_tempDir, "insert-active-name.txt");
        File.WriteAllText(filePath, "");
        var context = new EditorFileNameInsertionContext("active.txt", Path.Combine(_tempDir, "active.txt"), null, null);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, fileNameInsertionContext: context);

        Assert.Equal("active.txt", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlFInsertsEditedFilePath()
    {
        string filePath = Path.Combine(_tempDir, "insert-edited-path.txt");
        File.WriteAllText(filePath, "");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0006', ConsoleKey.F, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(filePath, File.ReadAllText(filePath));
    }

}
