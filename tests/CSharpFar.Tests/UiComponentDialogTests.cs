using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiComponentDialogTests
{
    [Fact]
    public void SelectionListDialog_EnterReturnsSelectedItem()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick");

        var result = dialog.Show(new ScreenRenderer(driver), PaletteRegistry.Default);

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
        Assert.Equal(1, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialog = new SelectionListDialog<string>(["one"], static item => item, "Pick");

        var result = dialog.Show(new ScreenRenderer(driver), PaletteRegistry.Default);

        Assert.False(result.IsConfirmed);
        Assert.Null(result.SelectedItem);
        Assert.Equal(-1, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_PageAndHomeEndUpdateSelectionAndScroll()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.PageUp));
        driver.EnqueueKey(Key(ConsoleKey.Home));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        };

        var result = dialog.Show(new ScreenRenderer(driver), PaletteRegistry.Default);

        Assert.True(result.IsConfirmed);
        Assert.Equal(0, result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
        Assert.Equal(0, dialog.ScrollTop);
    }

    [Fact]
    public void SelectionListDialog_MouseClickSelectsExpectedRow()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueInput(new MouseConsoleInputEvent(10, 5, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick");

        var result = dialog.Show(new ScreenRenderer(driver), PaletteRegistry.Default);

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
    }

    [Fact]
    public void SelectionListDialog_DrawsScrollbarOnlyWhenNeeded()
    {
        var noScrollDriver = new FakeConsoleDriver(40, 12);
        noScrollDriver.EnqueueKey(Key(ConsoleKey.Escape));
        new SelectionListDialog<string>(["one", "two"], static item => item, "Pick").Show(new ScreenRenderer(noScrollDriver), PaletteRegistry.Default);

        var scrollDriver = new FakeConsoleDriver(40, 12);
        scrollDriver.EnqueueKey(Key(ConsoleKey.Escape));
        new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        }.Show(new ScreenRenderer(scrollDriver), PaletteRegistry.Default);

        Assert.DoesNotContain(noScrollDriver.WriteRecords, write => write.Text.Contains('▲'));
        Assert.Contains(scrollDriver.WriteRecords, write => write.Text.Contains('▲'));
    }

    [Fact]
    public void DropdownSelect_KeyboardSelectionAndCancelKeepsPreviousValue()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var screen = new ScreenRenderer(driver);
        var dropdown = new DropdownSelect<string>(["utf-8", "utf-16", "ascii"], static item => item);
        var field = new Rect(5, 4, 12, 1);

        dropdown.Open(driver.GetSize(), field);
        Assert.True(dropdown.IsOpen);
        Assert.True(dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), driver.GetSize(), field, screen, out _));
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.True(dropdown.TryHandleKey(Key(ConsoleKey.Escape), driver.GetSize(), field, screen, out _));

        Assert.False(dropdown.IsOpen);
        Assert.Equal("utf-8", dropdown.SelectedItem);
    }

    [Fact]
    public void DropdownSelect_MouseSelectionAndScroll()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var screen = new ScreenRenderer(driver);
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 10).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        var field = new Rect(5, 4, 12, 1);
        dropdown.Open(driver.GetSize(), field);

        dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), driver.GetSize(), field, screen, out _);
        dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), driver.GetSize(), field, screen, out _);
        dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), driver.GetSize(), field, screen, out _);
        dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), driver.GetSize(), field, screen, out _);
        Assert.True(dropdown.ScrollTop > 0);

        Assert.True(dropdown.TryHandlePopupMouse(
            new MouseConsoleInputEvent(6, 7, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            screen,
            driver.GetSize(),
            field,
            out bool selected));
        Assert.True(selected);
        Assert.False(dropdown.IsOpen);
    }

    [Fact]
    public void ListWithButtonsDialog_ReturnsListDefaultAndButtonActions()
    {
        var listDriver = new FakeConsoleDriver(80, 20);
        listDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var listDialog = CreateListWithButtons(["alpha"]);

        var listResult = listDialog.Show(new ScreenRenderer(listDriver));

        Assert.NotNull(listResult);
        Assert.Equal("connect", listResult.ActionId);
        Assert.Equal("alpha", listResult.SelectedItem);

        var buttonDriver = new FakeConsoleDriver(80, 20);
        buttonDriver.EnqueueKey(Key(ConsoleKey.Tab));
        buttonDriver.EnqueueKey(Key(ConsoleKey.RightArrow));
        buttonDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var buttonDialog = CreateListWithButtons(["alpha"]);

        var buttonResult = buttonDialog.Show(new ScreenRenderer(buttonDriver));

        Assert.NotNull(buttonResult);
        Assert.Equal("delete", buttonResult.ActionId);
    }

    [Fact]
    public void ListWithButtonsDialog_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateListWithButtons(["alpha"]).Show(new ScreenRenderer(driver));

        Assert.Null(result);
    }

    private static ListWithButtonsDialog<string> CreateListWithButtons(IReadOnlyList<string> items) =>
        new(
            items,
            static item => item,
            [
                new DialogButton("connect", "Connect", 'O', IsDefault: true),
                new DialogButton("delete", "Delete", 'D'),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            "Items")
        {
            EmptyText = "Empty",
            DefaultListActionId = "connect",
            DeleteActionId = "delete",
            CancelActionId = "cancel",
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
