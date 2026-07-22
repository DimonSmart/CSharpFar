using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ChoiceRowTests
{
    [Theory]
    [InlineData(ConsoleKey.RightArrow, 0, 1)]
    [InlineData(ConsoleKey.LeftArrow, 1, 0)]
    [InlineData(ConsoleKey.LeftArrow, 0, 2)]
    [InlineData(ConsoleKey.RightArrow, 2, 0)]
    [InlineData(ConsoleKey.Spacebar, 2, 0)]
    [InlineData(ConsoleKey.Enter, 2, 0)]
    public void KeyboardMovement_CyclesSelection(ConsoleKey key, int selectedIndex, int expectedIndex)
    {
        var row = new ChoiceRow<string>(["one", "two", "three"], static value => value, selectedIndex);

        Assert.True(row.TryHandleKey(Key(key)));

        Assert.Equal(expectedIndex, row.SelectedIndex);
    }

    [Fact]
    public void MouseClickOnSimpleRow_SelectsNextItem()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        var layout = row.Render(Canvas(screen), 2, 1, 20, "Mode", focused: false);

        Assert.Equal(ChoiceRowLayoutKind.Simple, layout.Kind);
        Assert.True(row.TryHandleMouse(Mouse(3, 1), layout));

        Assert.Equal("two", row.Value);
    }

    [Theory]
    [InlineData("Default", 2, "Default")]
    [InlineData("Copy", 0, "Copy")]
    [InlineData("Inherit", 0, "Inherit")]
    public void ClickOnConcreteSegment_SelectsConcreteItem(string targetText, int selectedIndex, string expectedValue)
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value, selectedIndex);

        var layout = row.RenderSegmented(Canvas(screen),
            2,
            1,
            60,
            "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        int targetX = driver.GetRow(1).IndexOf(targetText, StringComparison.Ordinal);
        Assert.True(row.TryHandleMouse(Mouse(targetX, 1), layout));

        Assert.Equal(expectedValue, row.Value);
    }

    [Fact]
    public void SelectedMarkerBounds_FollowKeyboardSelection()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        var firstLayout = row.RenderSegmented(Canvas(screen), 2, 1, 60, string.Empty, true, FarDialogStyles.Fill, FarDialogStyles.FocusedInput);
        Assert.True(row.TryGetSelectedMarkerBounds(firstLayout, out Rect first));

        row.TryHandleKey(Key(ConsoleKey.RightArrow));
        var secondLayout = row.RenderSegmented(Canvas(screen), 2, 1, 60, string.Empty, true, FarDialogStyles.Fill, FarDialogStyles.FocusedInput);
        Assert.True(row.TryGetSelectedMarkerBounds(secondLayout, out Rect second));

        Assert.True(second.X > first.X);
        Assert.Equal(first.Y, second.Y);
    }

    [Fact]
    public void ClickOnConcreteSegment_SelectsItemRenderedOnEarlierSegmentedRow()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Ask", "Overwrite", "Skip", "Rename", "Only newer", "Reliable"], static value => value);
        var firstLayout = row.RenderSegmented(Canvas(screen),
            2,
            1,
            60,
            string.Empty,
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray),
            startIndex: 0,
            endIndex: 4);
        var secondLayout = row.RenderSegmented(Canvas(screen),
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
        var layout = new ChoiceRowLayout(
            ChoiceRowLayoutKind.Segmented,
            firstLayout.RowBounds.Concat(secondLayout.RowBounds).ToArray(),
            firstLayout.Choices.Concat(secondLayout.Choices).ToArray());
        Assert.True(row.TryHandleMouse(Mouse(renameX, 1), layout));

        Assert.Equal("Rename", row.Value);
    }

    [Fact]
    public void ClickOutsideSegment_DoesNotChangeValue()
    {
        var driver = new FakeConsoleDriver(80, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);
        var layout = row.RenderSegmented(Canvas(screen),
            2,
            1,
            60,
            "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        Assert.False(row.TryHandleMouse(Mouse(3, 1), layout));

        Assert.Equal("Default", row.Value);
    }

    [Fact]
    public void SegmentedLayout_WithAllChoicesClipped_ClickOnRowDoesNotCycle()
    {
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        var layout = row.CalculateSegmentedLayout(2, 1, 4, "Mode");

        Assert.Equal(ChoiceRowLayoutKind.Segmented, layout.Kind);
        Assert.Empty(layout.Choices);
        Assert.False(row.TryHandleMouse(Mouse(3, 1), layout));
        Assert.Equal(0, row.SelectedIndex);
    }

    [Fact]
    public void SegmentedLayout_ClickOnLabelDoesNotChangeSelection()
    {
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        var layout = row.CalculateSegmentedLayout(2, 1, 40, "Mode");

        Assert.False(row.TryHandleMouse(Mouse(3, 1), layout));

        Assert.Equal(0, row.SelectedIndex);
    }

    [Fact]
    public void SegmentedLayout_PartiallyClippedMarkerDoesNotReturnInvalidCursorBounds()
    {
        var row = new ChoiceRow<string>(["one"], static value => value);
        var layout = row.CalculateSegmentedLayout(1, 1, 2, string.Empty);

        Assert.True(row.TryGetSelectedMarkerBounds(layout, out Rect bounds));
        Assert.Equal(new Rect(1, 1, 2, 1), bounds);
    }

    [Fact]
    public void NonActivationMouse_DoesNotChangeValue()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["one", "two"], static value => value);
        var layout = row.Render(Canvas(screen), 2, 1, 20, "Mode", focused: false);

        Assert.False(row.TryHandleMouse(new MouseConsoleInputEvent(3, 1, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None), layout));

        Assert.Equal("one", row.Value);
    }

    [Fact]
    public void EmptyChoices_DoNotThrowAndIgnoreInput()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>([], static value => value);

        var layout = row.Render(Canvas(screen), 2, 1, 20, "Mode", focused: false);

        Assert.Equal(-1, row.SelectedIndex);
        Assert.False(row.TryHandleKey(Key(ConsoleKey.RightArrow)));
        Assert.False(row.TryHandleMouse(Mouse(3, 1), layout));
    }

    [Fact]
    public void SingleItem_DoesNotBreakKeyboardOrMouseHandling()
    {
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["only"], static value => value);
        var layout = row.Render(Canvas(screen), 2, 1, 20, "Mode", focused: false);

        Assert.False(row.TryHandleKey(Key(ConsoleKey.RightArrow)));
        Assert.True(row.TryHandleMouse(Mouse(3, 1), layout));

        Assert.Equal("only", row.Value);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
