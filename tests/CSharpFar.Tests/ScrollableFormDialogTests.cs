using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ScrollableFormDialogTests
{
    [Fact]
    public void InitialFocus_UsesFirstFocusableRow()
    {
        var form = new ScrollableFormDialog([
            new LabelRow("label", FarDialogStyles.Fill),
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);

        Render(form, visibleRows: 3);

        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void Navigation_SkipsNonFocusableRows()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new LabelRow("label", FarDialogStyles.Fill),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);
        Render(form, visibleRows: 3);

        form.HandleKey(Key(ConsoleKey.DownArrow));

        Assert.Equal(1, form.FocusIndex);
    }

    [Fact]
    public void DownAndUp_MoveBetweenFocusableRows()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);
        Render(form, visibleRows: 2);

        form.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, form.FocusIndex);

        form.HandleKey(Key(ConsoleKey.UpArrow));
        Assert.Equal(0, form.FocusIndex);
    }

    [Theory]
    [InlineData(ConsoleKey.Home, 0)]
    [InlineData(ConsoleKey.End, 2)]
    public void HomeAndEnd_MoveToBoundaryFocusableRows(ConsoleKey key, int expectedFocus)
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
        ]);
        Render(form, visibleRows: 3);

        form.HandleKey(Key(ConsoleKey.End));
        form.HandleKey(Key(key));

        Assert.Equal(expectedFocus, form.FocusIndex);
    }

    [Fact]
    public void TabAndShiftTab_MoveFocus()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);
        Render(form, visibleRows: 2);

        form.HandleKey(Key(ConsoleKey.Tab));
        Assert.Equal(1, form.FocusIndex);

        form.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void FocusMovement_MakesFocusedRowVisible()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
        ]);
        Render(form, visibleRows: 2);

        form.HandleKey(Key(ConsoleKey.End));

        Assert.Equal(3, form.FocusIndex);
        Assert.Equal(2, form.ScrollTop);
    }

    [Fact]
    public void FocusMovementUp_ScrollsBackToFocusedRow()
    {
        var form = LongForm();
        Render(form, visibleRows: 2);
        form.HandleKey(Key(ConsoleKey.End));

        form.HandleKey(Key(ConsoleKey.Home));

        Assert.Equal(0, form.FocusIndex);
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void Wheel_ClampsScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        form.HandleMouse(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.Equal(3, form.ScrollTop);

        form.HandleMouse(Mouse(2, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void Wheel_StaysWithinScrollBounds()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        for (int i = 0; i < 10; i++)
            form.HandleMouse(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.Equal(3, form.ScrollTop);

        for (int i = 0; i < 10; i++)
            form.HandleMouse(Mouse(2, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void PageDownAndPageUp_MoveFocusAndScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        form.HandleKey(Key(ConsoleKey.PageDown));
        Assert.True(form.FocusIndex >= 3);
        Assert.True(form.ScrollTop > 0);

        form.HandleKey(Key(ConsoleKey.PageUp));
        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void ClickVisibleFocusableRow_MovesFocus()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);
        Render(form, visibleRows: 2);

        form.HandleMouse(Mouse(2, 1));

        Assert.Equal(1, form.FocusIndex);
    }

    [Fact]
    public void ClickCheckbox_ChangesValue()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        var result = form.HandleMouse(Mouse(2, 0));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.True(checkbox.Value);
    }

    [Fact]
    public void ClickChoiceSegment_SelectsItem()
    {
        var choice = new ChoiceFormRow<string>(new ChoiceRow<string>(["one", "two"], static value => value), string.Empty);
        var form = new ScrollableFormDialog([choice]);
        var driver = Render(form, visibleRows: 1);
        int x = driver.GetRow(0).IndexOf("two", StringComparison.Ordinal);

        form.HandleMouse(Mouse(x, 0));

        Assert.Equal("two", choice.Value);
    }

    [Fact]
    public void ClickNonFocusableRow_DoesNotMoveFocus()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new LabelRow("label", FarDialogStyles.Fill),
        ]);
        Render(form, visibleRows: 2);

        form.HandleMouse(Mouse(2, 1));

        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void ClickOutsideBody_IsIgnored()
    {
        var form = new ScrollableFormDialog([new CheckBoxRow(new CheckBoxLine("one"))]);
        Render(form, visibleRows: 1);

        var result = form.HandleMouse(Mouse(2, 3));

        Assert.Equal(FormInputResultKind.NotHandled, result.Kind);
        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void RightClick_IsIgnoredByRows()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        form.HandleMouse(Mouse(2, 0, MouseButton.Right));

        Assert.False(checkbox.Value);
    }

    [Fact]
    public void ScrollbarClick_ChangesScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        form.HandleMouse(Mouse(19, 2));

        Assert.True(form.ScrollTop > 0);
    }

    [Fact]
    public void KeyDispatch_GoesToFocusedRow()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        form.HandleKey(Key(ConsoleKey.Spacebar));

        Assert.True(checkbox.Value);
    }

    [Fact]
    public void TextInput_DoesNotSwallowFormNavigationKeys()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([
            new TextInputRow(text),
            new CheckBoxRow(new CheckBoxLine("next")),
        ]);
        Render(form, visibleRows: 2);

        form.HandleKey(Key(ConsoleKey.DownArrow));

        Assert.Equal(1, form.FocusIndex);
        Assert.Equal(string.Empty, text.Text);
    }

    [Fact]
    public void TextInput_EditKeyChangesValue()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([new TextInputRow(text)]);
        Render(form, visibleRows: 1);

        var result = form.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Equal("a", text.Text);
    }

    [Fact]
    public void TextInputRowState_PreservesHistoryScrollbarDragAcrossRowRecreation()
    {
        var text = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < 20; i++)
            history.Add("item-" + i);
        Assert.True(history.OpenAll(availableContentRows: 5));

        var state = new TextInputRowState();
        var form = new ScrollableFormDialog([new TextInputRow(text, history, state)]);
        Render(form, visibleRows: 1, screenHeight: 8);

        form.HandleMouse(Mouse(19, 3, MouseButton.Left, MouseEventKind.Down));
        Assert.NotNull(state.HistoryScrollbarDrag);

        form.SetRows([new TextInputRow(text, history, state)]);
        form.HandleMouse(Mouse(19, 5, MouseButton.Left, MouseEventKind.Move));
        form.HandleMouse(Mouse(19, 5, MouseButton.Left, MouseEventKind.Up));

        Assert.Null(state.HistoryScrollbarDrag);
        Assert.True(history.FirstVisibleIndex > 0);
    }

    [Fact]
    public void ButtonRow_ReturnsSubmitAndCancel()
    {
        var form = new ScrollableFormDialog([
            new ButtonRow(
                [
                    new DialogButton("ok", "OK", 'O', IsDefault: true),
                    new DialogButton("cancel", "Cancel", 'C'),
                ],
                FarDialogStyles.Fill,
                FarDialogStyles.FocusedInput),
        ]);
        Render(form, visibleRows: 1);

        Assert.Equal(FormInputResultKind.Submit, form.HandleKey(Key(ConsoleKey.Enter)).Kind);

        form.HandleKey(Key(ConsoleKey.RightArrow));
        Assert.Equal(FormInputResultKind.Cancel, form.HandleKey(Key(ConsoleKey.Enter)).Kind);
    }

    private static ScrollableFormDialog LongForm() =>
        new([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
            new CheckBoxRow(new CheckBoxLine("six")),
        ]);

    private static FakeConsoleDriver Render(ScrollableFormDialog form, int visibleRows, int? screenHeight = null)
    {
        var driver = new FakeConsoleDriver(20, screenHeight ?? Math.Max(5, visibleRows + 2));
        var screen = new ScreenRenderer(driver);
        form.Render(new FormRenderContext(
            screen,
            new Rect(0, 0, 20, visibleRows),
            FarDialogStyles.Border));
        return driver;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(
        int x,
        int y,
        MouseButton button = MouseButton.Left,
        MouseEventKind kind = MouseEventKind.Down) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
