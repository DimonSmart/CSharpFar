using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ViewerFindDialogTests
{
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

        var result = new ViewerFindDialog(ModalTestHost.Create(driver), PaletteRegistry.Default)
            .Show(new ViewerSearchRequest(
                "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwj",
                CaseSensitive: false,
                WholeWords: false,
                UseRegex: false,
                SearchHex: false),
                hexMode: false);

        Assert.Null(result);
    }
}
