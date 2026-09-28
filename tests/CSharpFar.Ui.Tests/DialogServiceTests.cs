using CSharpFar.Console.Input;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class DialogServiceTests
{
    [Fact]
    public void SelectionDialogPresentation_StandardKeepsTheConventionalLimits()
    {
        Assert.Equal(60, SelectionDialogPresentation.Standard.MaxWidth);
        Assert.Equal(15, SelectionDialogPresentation.Standard.MaxVisibleRows);
    }

    [Fact]
    public void List_ContinueKeepsCurrentItemsAndSelectionWithoutReinvokingItems()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialogs = Create(driver);
        int itemCalls = 0;
        var actions = new ListDialogActionContext<string>[2];
        int actionCount = 0;

        string? result = dialogs.List(new ListDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => { itemCalls++; return ["one", "two", "three"]; },
            ItemText = static item => item,
            Actions = [DialogButton.Default("default", "Open", 'O')],
            HandleAction = action =>
            {
                actions[actionCount++] = action;
                return actionCount == 1 ? DialogOutcome<string>.ContinueOpen() : DialogOutcome<string>.Complete(action.SelectedItem!);
            },
            MaxVisibleRows = 1,
        });

        Assert.Equal("two", result);
        Assert.Equal(1, itemCalls);
        Assert.All(actions, action => Assert.Equal("two", action.SelectedItem));
        Assert.All(actions, action => Assert.Equal(1, action.SelectedIndex));
    }

    [Fact]
    public void List_RefreshReloadsItemsAndPreservesEqualSelectionOrUsesNearestIndex()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialogs = Create(driver);
        int itemCalls = 0;
        ListDialogActionContext<string>? closed = null;

        string? result = dialogs.List(new ListDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ++itemCalls == 1 ? ["one", "two", "three"] : ["zero", "two"],
            ItemText = static item => item,
            Actions = [DialogButton.Default("default", "Refresh", 'R')],
            HandleAction = action =>
            {
                if (itemCalls == 1)
                    return DialogOutcome<string>.RefreshOpen();
                closed = action;
                return DialogOutcome<string>.Complete(action.SelectedItem!);
            },
            MaxVisibleRows = 1,
        });

        Assert.Equal("two", result);
        Assert.Equal(2, itemCalls);
        Assert.Equal("two", closed?.SelectedItem);
        Assert.Equal(1, closed?.SelectedIndex);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void List_EscapeAndF10UseCancelResult(ConsoleKey key)
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(key));

        string? result = Create(driver).List(new ListDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ["one"],
            ItemText = static item => item,
            Actions = [DialogButton.Cancel()],
            HandleAction = _ => throw new InvalidOperationException("Cancelled dialogs do not invoke an action."),
            Cancel = () => "cancelled",
        });

        Assert.Equal("cancelled", result);
    }

    [Fact]
    public void Select_UsesSemanticOptionsAndReturnsTheInitialSelection()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
        var dialogs = new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

        var result = dialogs.Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["one", "two"],
            ItemText = static item => item,
            SelectedIndex = 1,
            MaxVisibleRows = 1,
            MaxWidth = 30,
            DoubleBorder = true,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
        Assert.Equal(1, result.SelectedIndex);
    }

    [Fact]
    public void Select_FilterReturnsOriginalIndexAndSelectionChangedUsesOriginalIndex()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(TextKey('g'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var selectedIndexes = new List<int>();

        var result = Create(driver).Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["Alpha", "Beta", "Gamma"],
            ItemText = static item => item,
            EnableFilter = true,
            SelectionChanged = (_, index) => selectedIndexes.Add(index),
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("Gamma", result.SelectedItem);
        Assert.Equal(2, result.SelectedIndex);
        Assert.Equal(2, selectedIndexes[^1]);
    }

    [Fact]
    public void Select_FilterUsesSearchTextInsteadOfDisplayText()
    {
        var driver = new FakeConsoleDriver();
        foreach (char ch in "sharp")
            driver.EnqueueKey(TextKey(ch));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = Create(driver).Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["C#", "JavaScript"],
            ItemText = static item => item,
            EnableFilter = true,
            SearchText = static item => item == "C#" ? "csharp source.cs" : "javascript source.js",
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("C#", result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
    }

    [Fact]
    public void Select_FilterSelectsFirstMatchWhenCurrentSelectionDisappears()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(TextKey('o'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = Create(driver).Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["one", "two", "three"],
            ItemText = static item => item,
            SelectedIndex = 2,
            EnableFilter = true,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("one", result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
    }

    [Fact]
    public void Select_FilterZeroMatchesEnterKeepsDialogOpenAndBackspaceRestoresItems()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(TextKey('z'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Backspace));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = Create(driver).Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["one"],
            ItemText = static item => item,
            EnableFilter = true,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("one", result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
    }

    [Fact]
    public void Form_DelegatesToTheOrdinaryFormFacade()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        var dialogs = new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

        string? result = dialogs.Form(
            new FormDialogOptions("Service", 30, 8),
            rows: () => [FormControls.Label("Body")],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<string?>.Complete(null)
                : FormDialogOutcome<string?>.Continue());

        Assert.Null(result);
    }

    private static DialogService Create(FakeConsoleDriver driver) => new(
        ModalTestHost.Create(driver),
        new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKeyInfo TextKey(char value) =>
        new(
            value,
            (ConsoleKey)((int)ConsoleKey.A + char.ToUpperInvariant(value) - 'A'),
            shift: false,
            alt: false,
            control: false);
}

