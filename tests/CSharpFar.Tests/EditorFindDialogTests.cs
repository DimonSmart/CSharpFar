using CSharpFar.App.Editor;
using CSharpFar.Console;
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

        var first = new EditorFindDialog(new ScreenRenderer(firstDriver), PaletteRegistry.Default).Show(null);
        Assert.Equal("abc", first?.Pattern);

        var secondDriver = new FakeConsoleDriver(80, 25);
        secondDriver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        secondDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var second = new EditorFindDialog(new ScreenRenderer(secondDriver), PaletteRegistry.Default).Show(null);

        Assert.Equal("abc", second?.Pattern);
    }
}
