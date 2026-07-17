using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CompactChoiceFormRowTests
{
    [Fact]
    public void LeftArrow_RightArrow_SpaceAndEnter_CycleChoices()
    {
        var row = Row(["one", "two", "three"], selectedIndex: 1);
        var input = new FormRowInputContext(0, focused: true);

        Assert.Equal(FormInputResultKind.ValueChanged, row.HandleKey(Key(ConsoleKey.LeftArrow), input).Kind);
        Assert.Equal("one", row.Value);
        Assert.Equal(FormInputResultKind.ValueChanged, row.HandleKey(Key(ConsoleKey.RightArrow), input).Kind);
        Assert.Equal("two", row.Value);
        Assert.Equal(FormInputResultKind.ValueChanged, row.HandleKey(Key(ConsoleKey.Spacebar), input).Kind);
        Assert.Equal("three", row.Value);
        Assert.Equal(FormInputResultKind.ValueChanged, row.HandleKey(Key(ConsoleKey.Enter), input).Kind);
        Assert.Equal("one", row.Value);
    }

    [Fact]
    public void KeyboardCyclesAcrossBoundaries()
    {
        var row = Row(["one", "two"], selectedIndex: 0);
        var input = new FormRowInputContext(0, focused: true);

        row.HandleKey(Key(ConsoleKey.LeftArrow), input);
        Assert.Equal("two", row.Value);

        row.HandleKey(Key(ConsoleKey.RightArrow), input);
        Assert.Equal("one", row.Value);
    }

    [Fact]
    public void SingleChoiceKeysAreHandledWithoutValueChange()
    {
        var row = Row(["only"]);
        var input = new FormRowInputContext(0, focused: true);

        Assert.Equal(FormInputResultKind.Handled, row.HandleKey(Key(ConsoleKey.RightArrow), input).Kind);
        Assert.Equal(FormInputResultKind.Handled, row.HandleKey(Key(ConsoleKey.Enter), input).Kind);
        Assert.Equal("only", row.Value);
    }

    [Fact]
    public void MouseClickCyclesSimpleChoice()
    {
        var row = Row(["one", "two"]);
        var context = new FormRowMouseContext(new Rect(10, 2, 30, 1), 0, true, 10);

        Assert.Equal(FormInputResultKind.ValueChanged, row.HandleMouse(Mouse(10, 2), context).Kind);

        Assert.Equal("two", row.Value);
    }

    [Fact]
    public void UnrecognizedKeyAndOutsideMouseAreNotHandled()
    {
        var row = Row(["one", "two"]);

        Assert.Equal(FormInputResultKind.NotHandled, row.HandleKey(Key(ConsoleKey.Tab), new FormRowInputContext(0, true)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleMouse(Mouse(0, 0), new FormRowMouseContext(new Rect(10, 2, 30, 1), 0, true, 10)).Kind);
        Assert.Equal("one", row.Value);
    }

    [Fact]
    public void CursorIsPlacedOnValueWhenFocusedAndClippedToBounds()
    {
        var row = Row(["Explicit FTPS"]);
        var driver = new FakeConsoleDriver(width: 50, height: 10);
        var screen = new ScreenRenderer(driver);
        var bounds = new Rect(10, 2, 12, 1);
        var context = new FormRowRenderContext(screen, bounds, focused: true);

        row.Render(context);

        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Mode:", StringComparison.Ordinal));
        Assert.True(row.TryGetCursor(context, out FormCursorPlacement cursor));
        Assert.InRange(cursor.X, bounds.X, bounds.Right - 1);
        Assert.Equal(bounds.Y, cursor.Y);
    }

    [Fact]
    public void CursorIsAbsentWhenNotFocused()
    {
        var row = Row(["one"]);
        var screen = new ScreenRenderer(new FakeConsoleDriver(width: 20, height: 4));

        Assert.False(row.TryGetCursor(new FormRowRenderContext(screen, new Rect(2, 1, 10, 1), focused: false), out FormCursorPlacement cursor));
        Assert.Equal(default, cursor);
    }

    [Fact]
    public void CursorIsAbsentWhenWidthIsZero()
    {
        var row = Row(["one"]);
        var screen = new ScreenRenderer(new FakeConsoleDriver(width: 20, height: 4));

        Assert.False(row.TryGetCursor(new FormRowRenderContext(screen, new Rect(2, 1, 0, 1), focused: true), out FormCursorPlacement cursor));
        Assert.Equal(default, cursor);
    }

    private static CompactChoiceFormRow<string> Row(IReadOnlyList<string> values, int selectedIndex = 0) =>
        new(new ChoiceRow<string>(values, static value => value, selectedIndex), "Mode");

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
