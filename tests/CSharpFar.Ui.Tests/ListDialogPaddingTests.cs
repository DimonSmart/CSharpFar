using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ListDialogPaddingTests
{
    [Fact]
    public void SelectionListDialog_RendersStandardPaddingAroundList()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(KeyInfo(ConsoleKey.Escape));

        new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick")
            .Show(ModalTestHost.Create(driver));

        Assert.Equal(' ', driver.GetCell(10, 3).Character);
        Assert.Equal(' ', driver.GetCell(9, 4).Character);
        Assert.Equal('o', driver.GetCell(10, 4).Character);
        Assert.Equal(' ', driver.GetCell(30, 4).Character);
        Assert.Equal(' ', driver.GetCell(10, 7).Character);
    }

    [Fact]
    public void SelectionListDialog_ClickOnPaddingDoesNotSelectItem()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueInput(new MouseConsoleInputEvent(9, 5, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(KeyInfo(ConsoleKey.Enter));

        SelectionListDialogResult<string> result = new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick")
            .Show(ModalTestHost.Create(driver));

        Assert.True(result.IsConfirmed);
        Assert.Equal("one", result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_UsesOnlyListViewScrollbarInsideRightPadding()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(KeyInfo(ConsoleKey.Escape));

        new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        }.Show(ModalTestHost.Create(driver));

        Assert.Equal('▲', driver.GetCell(29, 3).Character);
        Assert.Equal(' ', driver.GetCell(30, 3).Character);
        Assert.NotEqual('▲', driver.GetCell(31, 3).Character);
        Assert.Equal(1, CountCells(driver, '▲'));
    }

    [Fact]
    public void SelectionListDialog_ScrollbarMouseUsesCommittedListFrame()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueInput(new MouseConsoleInputEvent(29, 7, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(KeyInfo(ConsoleKey.Escape));
        var dialog = new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        };

        dialog.Show(ModalTestHost.Create(driver));

        Assert.True(dialog.ScrollTop > 0);
    }

    [Fact]
    public void SelectionListDialog_EmptyTextIsInsidePadding()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(KeyInfo(ConsoleKey.Enter));

        new SelectionListDialog<string>([], static item => item, "Pick")
        {
            EmptyText = "Nothing here",
        }.Show(ModalTestHost.Create(driver));

        Assert.Equal(' ', driver.GetCell(10, 4).Character);
        Assert.Equal(' ', driver.GetCell(9, 5).Character);
        Assert.Equal('N', driver.GetCell(10, 5).Character);
    }

    [Fact]
    public void ListWithButtonsDialog_RendersStandardPaddingWithoutReducingRequestedRows()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(KeyInfo(ConsoleKey.Escape));
        var dialog = new ListWithButtonsDialog<string>(
            ["alpha", "beta", "gamma"],
            static item => item,
            [
                new DialogButton("ok", "OK", 'O', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            ],
            "Items")
        {
            MaxVisibleRows = 3,
        };

        dialog.Show(ModalTestHost.Create(driver));

        Assert.Equal(' ', driver.GetCell(9, 6).Character);
        Assert.Equal(' ', driver.GetCell(8, 7).Character);
        Assert.Equal('a', driver.GetCell(9, 7).Character);
        Assert.Equal('b', driver.GetCell(9, 8).Character);
        Assert.Equal('g', driver.GetCell(9, 9).Character);
        Assert.Equal(' ', driver.GetCell(9, 10).Character);
        Assert.Equal(' ', driver.GetCell(9, 12).Character);
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("OK", StringComparison.Ordinal));
    }

    private static int CountCells(FakeConsoleDriver driver, char value)
    {
        ConsoleSize size = driver.GetSize();
        int count = 0;
        for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
                if (driver.GetCell(x, y).Character == value)
                    count++;
        return count;
    }

    private static ConsoleKeyInfo KeyInfo(ConsoleKey key) => UiTestInput.Key(key).Key;
}
