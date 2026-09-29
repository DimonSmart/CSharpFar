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
    public void Show_RendersInteractiveReplaceDialog()
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

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });
    }

    [Fact]
    public void Show_EnterFromFindExecutesFindAndKeepsDialogOpen()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);
        int findCalls = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ =>
            {
                findCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });

        Assert.Equal(1, findCalls);
        Assert.Equal(["foo"], history.Get(AppTextHistoryIds.EditorFindPattern).Items);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_InitialReplaceIsDisabled()
    {
        var driver = new FakeConsoleDriver(100, 30);
        bool queued = false;
        int replaceCalls = 0;
        driver.BeforeReadInput = currentDriver =>
        {
            if (queued)
                return;

            EnqueueButtonClick(currentDriver, "Replace");
            currentDriver.EnqueueKey(Key(ConsoleKey.Escape));
            queued = true;
        };
        var (dialog, _) = CreateDialog(driver);

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) =>
            {
                replaceCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });

        Assert.Equal(0, replaceCalls);
    }

    [Fact]
    public void Show_SuccessfulFindEnablesReplaceAndReplaceKeepsDialogOpen()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);
        int findCalls = 0;
        int replaceCalls = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "  bar  ",
            _ =>
            {
                findCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, replacement) =>
            {
                replaceCalls++;
                Assert.Equal("  bar  ", replacement);
                return EditorReplaceCommandResult.Completed("No more matches.");
            },
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });

        Assert.Equal(1, findCalls);
        Assert.Equal(1, replaceCalls);
        Assert.Equal(["  bar  "], history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_ReplaceCanImmediatelyEnableNextPreview()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, _) = CreateDialog(driver);
        int replaceCalls = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) =>
            {
                replaceCalls++;
                return replaceCalls == 1
                    ? EditorReplaceCommandResult.Success()
                    : EditorReplaceCommandResult.Completed("No more matches.");
            },
            (_, _) => EditorReplaceCommandResult.Completed(),
            () => { });

        Assert.Equal(2, replaceCalls);
    }

    [Fact]
    public void Show_AfterSuccessfulFindRendersFindNext()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        bool sawFindNext = false;
        int findCalls = 0;
        driver.BeforeReadInput = currentDriver =>
        {
            if (findCalls == 1)
            {
                string rendered = string.Join("\n", currentDriver.WriteRecords.Select(record => record.Text));
                sawFindNext |= rendered.Contains("Find next", StringComparison.Ordinal);
            }
        };
        var (dialog, _) = CreateDialog(driver);

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ =>
            {
                findCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, _) => EditorReplaceCommandResult.Completed(),
            (_, _) => EditorReplaceCommandResult.Completed(),
            () => { });

        Assert.True(sawFindNext);
    }

    [Fact]
    public void Show_SearchCriteriaChangeInvalidatesPreview()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, _) = CreateDialog(driver);
        int invalidations = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => invalidations++);

        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void Show_ReplacementChangeDoesNotInvalidatePreview()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, _) = CreateDialog(driver);
        int invalidations = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => invalidations++);

        Assert.Equal(0, invalidations);
    }

    [Fact]
    public void Show_InvalidRegexDoesNotCallFindOrCommitHistory()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);
        int findCalls = 0;

        dialog.Show(
            new EditorSearchOptions("[", UseRegex: true),
            "bar",
            _ =>
            {
                findCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });

        Assert.Equal(0, findCalls);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorFindPattern).Items);
        Assert.Empty(history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_ReplaceAllDoesNotRequireFindAndCommitsHistories()
    {
        var driver = new FakeConsoleDriver(100, 30);
        bool queued = false;
        driver.BeforeReadInput = currentDriver =>
        {
            if (queued)
                return;

            EnqueueButtonClick(currentDriver, "Replace all");
            queued = true;
        };
        var (dialog, history) = CreateDialog(driver);
        int findCalls = 0;
        int replaceAllCalls = 0;

        dialog.Show(
            new EditorSearchOptions("foo"),
            "  ",
            _ =>
            {
                findCalls++;
                return EditorReplaceCommandResult.Success();
            },
            (_, _) => EditorReplaceCommandResult.Success(),
            (search, replacement) =>
            {
                replaceAllCalls++;
                Assert.Equal("foo", search.Pattern);
                Assert.Equal("  ", replacement);
                return EditorReplaceCommandResult.Success();
            },
            () => { });

        Assert.Equal(0, findCalls);
        Assert.Equal(1, replaceAllCalls);
        Assert.Equal(["foo"], history.Get(AppTextHistoryIds.EditorFindPattern).Items);
        Assert.Equal(["  "], history.Get(AppTextHistoryIds.EditorReplaceText).Items);
    }

    [Fact]
    public void Show_CancelDoesNotCommitHistory()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var (dialog, history) = CreateDialog(driver);

        dialog.Show(
            new EditorSearchOptions("foo"),
            "bar",
            _ => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            (_, _) => EditorReplaceCommandResult.Success(),
            () => { });

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

    private static void EnqueueButtonClick(FakeConsoleDriver driver, string text)
    {
        var row = driver.WriteRecords.Last(record =>
            record.Text.Contains(text, StringComparison.Ordinal));
        int x = row.X + row.Text.IndexOf(text, StringComparison.Ordinal);
        driver.EnqueueInput(new MouseConsoleInputEvent(
            x,
            row.Y,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            x,
            row.Y,
            MouseButton.Left,
            MouseEventKind.Up,
            MouseKeyModifiers.None));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
