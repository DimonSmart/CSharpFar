using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class EditorReplaceDialogTests
{
    [Fact]
    public void Show_RendersUnifiedReplaceDialog()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.BeforeReadInput = currentDriver =>
        {
            string rendered = string.Join("\n", currentDriver.WriteRecords.Select(record => record.Text));
            Assert.Contains("Find", rendered);
            Assert.Contains("Replace with", rendered);
            Assert.Contains("Case sensitive", rendered);
            Assert.Contains("Whole words", rendered);
            Assert.Contains("Regular expressions", rendered);
            Assert.Contains("Search backwards", rendered);
            Assert.Contains("Replace", rendered);
            Assert.Contains("Replace all", rendered);
            Assert.Contains("Cancel", rendered);
        };

        var (dialog, _) = CreateDialog(driver);

        Assert.Null(dialog.Show(new EditorSearchOptions("foo"), "bar"));
    }

    [Fact]
    public void Show_EnterUsesReplaceActionAndAllowsEmptyReplacement()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var (dialog, _) = CreateDialog(driver);

        var result = dialog.Show(
            new EditorSearchOptions("foo", SearchBackward: true, UseRegex: true),
            string.Empty);

        Assert.NotNull(result);
        Assert.Equal(EditorReplaceAction.Replace, result.Action);
        Assert.True(result.Search.SearchBackward);
        Assert.True(result.Search.UseRegex);
        Assert.Equal(string.Empty, result.Replacement);
    }

    [Fact]
    public void Show_ReplaceAllButtonReturnsReplaceAllAction()
    {
        var driver = new FakeConsoleDriver(100, 30);
        bool queued = false;
        driver.BeforeReadInput = currentDriver =>
        {
            if (queued)
                return;

            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Replace all", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Replace all", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(
                x,
                row.Y,
                MouseButton.Left,
                MouseEventKind.Down,
                MouseKeyModifiers.None));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(
                x,
                row.Y,
                MouseButton.Left,
                MouseEventKind.Up,
                MouseKeyModifiers.None));
            queued = true;
        };
        var (dialog, _) = CreateDialog(driver);

        var result = dialog.Show(new EditorSearchOptions("foo"), "bar");

        Assert.NotNull(result);
        Assert.Equal(EditorReplaceAction.ReplaceAll, result.Action);
    }

    [Fact]
    public void Show_WhitespaceReplacementIsNotTrimmedAndIsStoredVerbatim()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var (dialog, history) = CreateDialog(driver);

        var result = dialog.Show(new EditorSearchOptions("foo"), "  bar  ");

        Assert.NotNull(result);
        Assert.Equal("  bar  ", result.Replacement);
        Assert.Equal(
            ["  bar  "],
            history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_WhitespaceOnlyReplacementIsStoredAndReloadable()
    {
        var store = new InMemorySingleLineTextHistoryStore();
        var history = new SingleLineTextHistoryRegistry(store);
        var firstDriver = new FakeConsoleDriver(100, 30);
        firstDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var first = CreateDialog(firstDriver, history);

        var result = first.Show(new EditorSearchOptions("foo"), "  ");

        Assert.NotNull(result);
        Assert.Equal("  ", Assert.Single(history.Get(AppTextHistoryIds.EditorReplaceText).Items));

        var reloaded = new SingleLineTextHistoryRegistry(store);
        Assert.Equal("  ", Assert.Single(reloaded.Get(AppTextHistoryIds.EditorReplaceText).Items));
    }

    [Fact]
    public void Show_CancelDoesNotCommitHistory()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);

        Assert.Null(dialog.Show(new EditorSearchOptions("foo"), "bar"));
        Assert.Empty(history.Get(AppTextHistoryIds.EditorFindPattern).Items);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_InvalidRegexDoesNotCloseOrCommitHistory()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);

        var result = dialog.Show(new EditorSearchOptions("[", UseRegex: true), "bar");

        Assert.Null(result);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorFindPattern).Items);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    private static (EditorReplaceDialog Dialog, SingleLineTextHistoryRegistry History) CreateDialog(
        FakeConsoleDriver driver)
    {
        var history = new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
        return (CreateDialog(driver, history), history);
    }

    private static EditorReplaceDialog CreateDialog(
        FakeConsoleDriver driver,
        SingleLineTextHistoryRegistry history)
    {
        var fields = new FormFieldFactory(history);
        return new EditorReplaceDialog(
            new DialogService(ModalTestHost.Create(driver), fields),
            fields);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
