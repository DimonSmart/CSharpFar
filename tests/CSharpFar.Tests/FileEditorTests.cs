using CSharpFar.App.Editor;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class FileEditorTests : IDisposable
{
    private readonly string _tempDir;

    public FileEditorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarEditorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (string path in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Show_ReadOnlyFileIgnoresTypingAndExitsCleanly()
    {
        string filePath = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_WhenReadOnlyLockDisabled_StaysOpenWhenSaveOnExitFails()
    {
        string filePath = Path.Combine(_tempDir, "readonly-save.txt");
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('S', ConsoleKey.S, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('D', ConsoleKey.D, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            new AppSettings.EditorSettings { OpenReadOnlyFilesReadOnly = false });

        Assert.Throws<InvalidOperationException>(() => driver.ReadKey(intercept: true));
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ModifierInputSwitchesEditorFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "keys.txt");
        File.WriteAllText(filePath, "text");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Prev", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_FunctionKeyBarMouseClickSavesAndClosesEditor()
    {
        string filePath = Path.Combine(_tempDir, "mouse-keybar.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(width: 120, height: 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueInput(LeftMouse(x: 10, y: 24));
        driver.EnqueueInput(LeftMouse(x: 90, y: 24));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("Xabc", File.ReadAllText(filePath));
        Assert.Contains("10Close", ComposeRow(driver, y: 24, width: 120));
    }

    [Fact]
    public void Show_MouseWheelScrollsTextDown()
    {
        string filePath = Path.Combine(_tempDir, "editor-wheel.txt");
        File.WriteAllText(filePath, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")));

        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueInput(MouseWheelDown());
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains("line4", string.Concat(driver.WriteRecords.Select(record => record.Text)));
    }

    [Fact]
    public void Show_F3MarkAndF6Move_CutSelectedText()
    {
        string filePath = Path.Combine(_tempDir, "mark-move.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("c", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_F5CopyAndPaste_UsesSelectedText()
    {
        string filePath = Path.Combine(_tempDir, "mark-copy.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("a", clipboard.Text);
        Assert.Equal("abca", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlC_CopiesSelectionToSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "system-copy.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("ab", clipboard.Text);
    }

    [Fact]
    public void Show_CtrlInsert_CopiesSelectionToSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-insert-copy.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Insert, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("a", clipboard.Text);
    }

    [Fact]
    public void Show_ShiftDelete_CutsSelectionToSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "shift-delete-cut.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("a", clipboard.Text);
        Assert.Equal("bc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlV_PastesFromSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "system-paste.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard { Text = "Z" };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("abcZ", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ShiftInsert_PastesFromSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "shift-insert-paste.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard { Text = "Z" };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Insert, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("abcZ", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlV_WithEmptySystemClipboard_DoesNotPastePreviousCopy()
    {
        string filePath = Path.Combine(_tempDir, "empty-system-clipboard.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard { ClearAfterSet = true };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("abc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlAAndCtrlC_CopiesUtf8BomFileTextWithoutBom()
    {
        string filePath = Path.Combine(_tempDir, "system-copy-bom.json");
        File.WriteAllText(filePath, "{\"items\":[1,2]}", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("{\"items\":[1,2]}", clipboard.Text);
        Assert.False(clipboard.Text.StartsWith('\uFEFF'));
    }

    [Fact]
    public void Show_CtrlAAndCtrlC_NormalizesSystemClipboardLineEndingsOnWindows()
    {
        string filePath = Path.Combine(_tempDir, "system-copy-lines.txt");
        File.WriteAllText(filePath, "a\nb");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        string expected = OperatingSystem.IsWindows() ? "a\r\nb" : "a\nb";
        Assert.Equal(expected, clipboard.Text);
    }

    [Fact]
    public void Show_CtrlASelectsAllText()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-a.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("X", File.ReadAllText(filePath));
    }

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

    [Fact]
    public void Show_SelectionOnEmptyLine_IsRenderedWithInvertedStyle()
    {
        string filePath = Path.Combine(_tempDir, "empty-selection.txt");
        File.WriteAllText(filePath, "a\n\nb");
        var palette = PaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 2 &&
            record.Background == palette.CommandLineFg);
    }

    [Fact]
    public void Show_CursorAfterLineEnd_ShowsLineEndingInStatus()
    {
        string filePath = Path.Combine(_tempDir, "line-ending-status.txt");
        File.WriteAllText(filePath, "a\r\nb");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("CRLF", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("Col 2  EOF", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_CursorAtDocumentEnd_StillShowsEofInStatus()
    {
        string filePath = Path.Combine(_tempDir, "eof-status.txt");
        File.WriteAllText(filePath, "a");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("EOF", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_RightArrowDoesNotSplitUtf8FourByteCharacter()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte.txt");
        File.WriteAllText(filePath, "A" + smile + "B");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("A" + smile + "XB", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ScreenCursorUsesEmojiDisplayWidth()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-cursor-column.txt");
        File.WriteAllText(filePath, "A" + smile + "B");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(3, driver.CursorX);
    }

    [Fact]
    public void Show_WideEmojiCursorUsesFullGlyphStyleAndHidesNativeCursor()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-custom-cursor.txt");
        File.WriteAllText(filePath, "A" + smile + "B");
        var palette = PaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 1 &&
            record.Y == 1 &&
            record.Text == smile &&
            record.Background == palette.CommandLineFg);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void Show_WideEmojiCursorBlinksByTogglingFullGlyphStyle()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-blink-cursor.txt");
        File.WriteAllText(filePath, "A" + smile + "B");
        var palette = PaletteRegistry.Default;

        bool sawCursorOn = false;
        bool sawCursorOffAfterOn = false;
        int polls = 0;
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeReadInput = currentDriver =>
        {
            currentDriver.BeforeTryReadInput = ObserveBlink;
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.True(sawCursorOn);
        Assert.True(sawCursorOffAfterOn);

        void ObserveBlink(FakeConsoleDriver currentDriver)
        {
            polls++;
            bool cursorOn =
                currentDriver.GetCell(1, 1).Background == palette.CommandLineFg &&
                currentDriver.GetCell(2, 1).Background == palette.CommandLineFg;
            bool cursorOff =
                currentDriver.GetCell(1, 1).Background == palette.PanelBackground &&
                currentDriver.GetCell(2, 1).Background == palette.PanelBackground;

            sawCursorOn |= cursorOn;
            sawCursorOffAfterOn |= sawCursorOn && cursorOff;
            if (sawCursorOffAfterOn || polls > 60)
            {
                currentDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
                return;
            }

            currentDriver.BeforeTryReadInput = ObserveBlink;
        }
    }

    [Fact]
    public void Show_StatusReportsUtf8FourByteCharacterCode()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-status.txt");
        File.WriteAllText(filePath, smile);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("Col 1", StringComparison.Ordinal) &&
            record.Text.Contains("U+1F642/128578", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_EnterAtEndOfUtf8FourByteLineMovesCursorToNextLineStart()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string grin = char.ConvertFromUtf32(0x1F600);
        string gothicLetter = char.ConvertFromUtf32(0x10348);
        string line = "ascii A " + smile + " " + grin + " " + gothicLetter + " Z";
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-enter-at-end.txt");
        File.WriteAllText(filePath, line);

        int cursorXAfterEnter = -1;
        int cursorYAfterEnter = -1;
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeReadInput = currentDriver =>
        {
            currentDriver.BeforeReadInput = afterEnd =>
            {
                afterEnd.BeforeReadInput = afterEnter =>
                {
                    cursorXAfterEnter = afterEnter.CursorX;
                    cursorYAfterEnter = afterEnter.CursorY;
                };
            };
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(0, cursorXAfterEnter);
        Assert.Equal(2, cursorYAfterEnter);
        Assert.Equal(line + "\nX", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_SyntaxSpanAppliesTokenStyle()
    {
        string filePath = Path.Combine(_tempDir, "syntax-render.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter(
            new EditorColorSpan(0, 1, 1, new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black)));

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, syntaxHighlighter: highlighter);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 1 &&
            record.Y == 1 &&
            record.Text == "b" &&
            record.Foreground == ConsoleColor.Yellow);
    }

    [Fact]
    public void Show_PlainEditorTextUsesPanelBackground()
    {
        string filePath = Path.Combine(_tempDir, "plain-editor-background.unknown");
        File.WriteAllText(filePath, "abc");
        var palette = PaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 0 &&
            record.Y == 1 &&
            record.Text.StartsWith('a') &&
            record.Background == palette.PanelBackground);
    }

    [Fact]
    public void Show_SelectionStyleOverridesSyntaxSpan()
    {
        string filePath = Path.Combine(_tempDir, "syntax-selection.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter(
            new EditorColorSpan(0, 0, 3, new CellStyle(ConsoleColor.Red, ConsoleColor.Black)));
        var palette = PaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, syntaxHighlighter: highlighter);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 0 &&
            record.Y == 1 &&
            record.Text == "a" &&
            record.Background == palette.CommandLineFg);
    }

    private static void ShowFileEditor(
        ScreenRenderer renderer,
        string filePath,
        AppSettings.EditorSettings? settings = null,
        ITextClipboard? clipboard = null,
        EditorFileNameInsertionContext? fileNameInsertionContext = null,
        IEditorSyntaxHighlighter? syntaxHighlighter = null)
    {
        var editor = new FileEditor(
            renderer,
            null,
            settings,
            clipboard,
            fileNameInsertionContext,
            syntaxHighlighter);
        editor.Show(filePath);
    }

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent MouseWheelDown() =>
        new(0, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None);

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

    private sealed class FakeTextClipboard : ITextClipboard
    {
        public string Text { get; set; } = string.Empty;
        public bool ClearAfterSet { get; set; }

        public bool TrySetText(string text)
        {
            Text = text;
            if (ClearAfterSet)
                Text = string.Empty;
            return true;
        }

        public bool TryGetText(out string text)
        {
            text = Text;
            return true;
        }
    }

    private sealed class StaticSyntaxHighlighter : IEditorSyntaxHighlighter
    {
        private readonly IReadOnlyList<EditorColorSpan> _spans;

        public StaticSyntaxHighlighter(params EditorColorSpan[] spans)
        {
            _spans = spans;
        }

        public EditorSyntaxHighlightResult Highlight(EditorSyntaxHighlightRequest request) =>
            new(_spans, EditorSyntaxDiagnostics.Active("Fake", "Fake", "palette"));
    }
}
