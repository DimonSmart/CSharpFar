using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ChoiceRowTests
{
    [Fact]
    public void RightArrow_SelectsNextItem()
    {
        var row = new ChoiceRow<string>(["one", "two", "three"], static value => value);

        Assert.True(row.TryHandleKey(Key(ConsoleKey.RightArrow)));

        Assert.Equal("two", row.Value);
    }

    [Fact]
    public void LeftArrow_SelectsPreviousItem()
    {
        var row = new ChoiceRow<string>(["one", "two", "three"], static value => value, selectedIndex: 1);

        Assert.True(row.TryHandleKey(Key(ConsoleKey.LeftArrow)));

        Assert.Equal("one", row.Value);
    }

    [Theory]
    [InlineData(ConsoleKey.Spacebar)]
    [InlineData(ConsoleKey.Enter)]
    public void SpaceAndEnter_SelectNextItem(ConsoleKey key)
    {
        var row = new ChoiceRow<string>(["one", "two"], static value => value);

        Assert.True(row.TryHandleKey(Key(key)));

        Assert.Equal("two", row.Value);
    }

    [Fact]
    public void MouseClickOnSimpleRow_SelectsNextItem()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        row.Render(screen, 2, 1, 20, "Mode", focused: false);

        Assert.True(row.TryHandleMouse(Mouse(3, 1)));

        Assert.Equal("two", row.Value);
    }

    [Fact]
    public void RenderSegmented_SavesChoiceBounds()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);

        row.RenderSegmented(
            screen,
            2,
            1,
            60,
            "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        int copyX = driver.GetRow(1).IndexOf("Copy", StringComparison.Ordinal);
        Assert.True(row.TryHandleMouse(Mouse(copyX, 1)));
        Assert.Equal("Copy", row.Value);
    }

    [Fact]
    public void SelectedMarkerBounds_FollowKeyboardSelection()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        row.RenderSegmented(screen, 2, 1, 60, string.Empty, true, FarDialogStyles.Fill, FarDialogStyles.FocusedInput);
        Assert.True(row.TryGetSelectedMarkerBounds(out Rect first));

        row.TryHandleKey(Key(ConsoleKey.RightArrow));
        row.RenderSegmented(screen, 2, 1, 60, string.Empty, true, FarDialogStyles.Fill, FarDialogStyles.FocusedInput);
        Assert.True(row.TryGetSelectedMarkerBounds(out Rect second));

        Assert.True(second.X > first.X);
        Assert.Equal(first.Y, second.Y);
    }

    [Fact]
    public void ClickOnConcreteSegment_SelectsConcreteItem()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);
        row.RenderSegmented(
            screen,
            2,
            1,
            60,
            string.Empty,
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        int inheritX = driver.GetRow(1).IndexOf("Inherit", StringComparison.Ordinal);
        Assert.True(row.TryHandleMouse(Mouse(inheritX, 1)));

        Assert.Equal("Inherit", row.Value);
    }

    [Fact]
    public void ClickOnConcreteSegment_SelectsItemRenderedOnEarlierSegmentedRow()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Ask", "Overwrite", "Skip", "Rename", "Only newer", "Reliable"], static value => value);
        row.RenderSegmented(
            screen,
            2,
            1,
            60,
            string.Empty,
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray),
            startIndex: 0,
            endIndex: 4);
        row.RenderSegmented(
            screen,
            2,
            2,
            60,
            string.Empty,
            focused: false,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray),
            startIndex: 4,
            endIndex: 6);

        int renameX = driver.GetRow(1).IndexOf("Rename", StringComparison.Ordinal);
        Assert.True(row.TryHandleMouse(Mouse(renameX, 1)));

        Assert.Equal("Rename", row.Value);
    }

    [Fact]
    public void ClickOutsideSegment_DoesNotChangeValue()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);
        row.RenderSegmented(
            screen,
            2,
            1,
            60,
            "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        Assert.False(row.TryHandleMouse(Mouse(3, 1)));

        Assert.Equal("Default", row.Value);
    }

    [Fact]
    public void NonActivationMouse_DoesNotChangeValue()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        row.Render(screen, 2, 1, 20, "Mode", focused: false);

        Assert.False(row.TryHandleMouse(new MouseConsoleInputEvent(3, 1, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None)));

        Assert.Equal("one", row.Value);
    }

    [Fact]
    public void EmptyChoices_DoNotThrowAndIgnoreInput()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>([], static value => value);

        row.Render(screen, 2, 1, 20, "Mode", focused: false);

        Assert.Equal(-1, row.SelectedIndex);
        Assert.False(row.TryHandleKey(Key(ConsoleKey.RightArrow)));
        Assert.False(row.TryHandleMouse(Mouse(3, 1)));
    }

    [Fact]
    public void SingleItem_DoesNotBreakKeyboardOrMouseHandling()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["only"], static value => value);
        row.Render(screen, 2, 1, 20, "Mode", focused: false);

        Assert.False(row.TryHandleKey(Key(ConsoleKey.RightArrow)));
        Assert.True(row.TryHandleMouse(Mouse(3, 1)));

        Assert.Equal("only", row.Value);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
