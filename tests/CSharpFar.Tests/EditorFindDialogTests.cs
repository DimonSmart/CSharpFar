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
        var historyRegistry = CreateHistoryProvider();
        var firstDriver = new FakeConsoleDriver(80, 25);
        firstDriver.EnqueueKey(Key('a', ConsoleKey.A));
        firstDriver.EnqueueKey(Key('b', ConsoleKey.B));
        firstDriver.EnqueueKey(Key('c', ConsoleKey.C));
        firstDriver.EnqueueKey(Key(ConsoleKey.Enter));
        firstDriver.EnqueueKey(Key(ConsoleKey.Enter));

        var first = new EditorFindDialog(
            new DialogService(ModalTestHost.Create(firstDriver), new FormFieldFactory(historyRegistry))).Show(null);
        Assert.Equal("abc", first?.Pattern);

        var secondDriver = new FakeConsoleDriver(80, 25);
        secondDriver.EnqueueKey(Key('a', ConsoleKey.A));
        secondDriver.EnqueueKey(Key(ConsoleKey.DownArrow));
        secondDriver.EnqueueKey(Key(ConsoleKey.Enter));
        secondDriver.EnqueueKey(Key(ConsoleKey.Enter));

        var second = new EditorFindDialog(
            new DialogService(ModalTestHost.Create(secondDriver), new FormFieldFactory(historyRegistry))).Show(null);

        Assert.Equal("abc", second?.Pattern);
    }

    [Fact]
    public void Show_LongPatternAtEnd_PutsCursorInBlankCellAfterLastCharacter()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.BeforeReadInput = currentDriver =>
        {
            Assert.Equal('j', currentDriver.GetCell(currentDriver.CursorX - 1, currentDriver.CursorY).Character);
            Assert.Equal(' ', currentDriver.GetCell(currentDriver.CursorX, currentDriver.CursorY).Character);
        };

        var result = CreateDialog(driver).Show(new EditorSearchOptions(
            "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrsj"));

        Assert.Null(result);
    }

    [Fact]
    public void Show_RendersAllEditorSearchOptions()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.BeforeReadInput = currentDriver =>
        {
            string rendered = string.Join("\n", currentDriver.WriteRecords.Select(record => record.Text));
            Assert.Contains("Case sensitive", rendered);
            Assert.Contains("Whole words", rendered);
            Assert.Contains("Regular expressions", rendered);
            Assert.Contains("Search backwards", rendered);
        };

        Assert.Null(CreateDialog(driver).Show(new EditorSearchOptions("abc")));
    }

    [Fact]
    public void Show_PreservesAndReturnsAllOptions()
    {
        var previous = new EditorSearchOptions(
            "abc",
            SearchBackward: true,
            CaseSensitive: true,
            WholeWords: true,
            UseRegex: true);
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = CreateDialog(driver).Show(previous);

        Assert.Equal(previous, result);
    }

    [Fact]
    public void Show_InvalidRegex_DoesNotCloseDialog()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateDialog(driver).Show(new EditorSearchOptions("[", UseRegex: true));

        Assert.Null(result);
    }

    private static EditorFindDialog CreateDialog(FakeConsoleDriver driver) =>
        new(new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(CreateHistoryProvider())));

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo Key(char ch, ConsoleKey key) =>
        new(ch, key, shift: false, alt: false, control: false);

    private static ITextFieldHistoryProvider CreateHistoryProvider() =>
        new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
}
