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

}
