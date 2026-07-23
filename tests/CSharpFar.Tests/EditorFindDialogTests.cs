using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class EditorFindDialogTests
{
    [Fact]
    public void Show_UsesInputHistory()
    {
        var firstDriver = new FakeConsoleDriver(80, 25);
        firstDriver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        firstDriver.EnqueueKey(new ConsoleKeyInfo('b', ConsoleKey.B, shift: false, alt: false, control: false));
        firstDriver.EnqueueKey(new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false));
        firstDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        firstDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var first = new EditorFindDialog(ModalTestHost.Create(firstDriver), PaletteRegistry.Default).Show(null);
        Assert.Equal("abc", first?.Pattern);

        var secondDriver = new FakeConsoleDriver(80, 25);
        secondDriver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var second = new EditorFindDialog(ModalTestHost.Create(secondDriver), PaletteRegistry.Default).Show(null);

        Assert.Equal("abc", second?.Pattern);
    }

    [Fact]
    public void Show_LongPatternAtEnd_PutsCursorInBlankCellAfterLastCharacter()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));
        driver.BeforeReadInput = currentDriver =>
        {
            Assert.Equal('j', currentDriver.GetCell(currentDriver.CursorX - 1, currentDriver.CursorY).Character);
            Assert.Equal(' ', currentDriver.GetCell(currentDriver.CursorX, currentDriver.CursorY).Character);
        };

        var result = new EditorFindDialog(ModalTestHost.Create(driver), PaletteRegistry.Default)
            .Show(new EditorFindDialogResult(
                "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrsj",
                CaseSensitive: false,
                WholeWords: false));

        Assert.Null(result);
    }

    [Fact]
    public void Show_MouseClickCheckboxTogglesOption()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new MouseConsoleInputEvent(15, 11, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(35, 14, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(35, 14, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        var result = new EditorFindDialog(ModalTestHost.Create(driver), PaletteRegistry.Default)
            .Show(new EditorFindDialogResult("abc", CaseSensitive: false, WholeWords: false));

        Assert.NotNull(result);
        Assert.True(result.CaseSensitive);
        Assert.False(result.WholeWords);
    }
}
