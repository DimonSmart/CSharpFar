using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void Navigation_SkipsNonFocusableRows()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new LabelRow("label", DialogStyles.Fill),
            new CheckBoxRow(new CheckBoxLine("two")),
        ]);
        Render(form, visibleRows: 3);

        HandleKey(form, Key(ConsoleKey.DownArrow));

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

        HandleKey(form, Key(ConsoleKey.DownArrow));
        Assert.Equal(1, form.FocusIndex);

        HandleKey(form, Key(ConsoleKey.UpArrow));
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

        HandleKey(form, Key(ConsoleKey.End));
        HandleKey(form, Key(key));

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

        HandleKey(form, Key(ConsoleKey.Tab));
        Assert.Equal(1, form.FocusIndex);

        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
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

        HandleKey(form, Key(ConsoleKey.End));

        Assert.Equal(3, form.FocusIndex);
        Assert.Equal(2, form.ScrollTop);
    }

    [Fact]
    public void FocusMovementUp_ScrollsBackToFocusedRow()
    {
        var form = LongForm();
        Render(form, visibleRows: 2);
        HandleKey(form, Key(ConsoleKey.End));

        HandleKey(form, Key(ConsoleKey.Home));

        Assert.Equal(0, form.FocusIndex);
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void Wheel_ClampsScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        HandleMouse(form, Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.Equal(3, form.ScrollTop);

        HandleMouse(form, Mouse(2, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void Wheel_StaysWithinScrollBounds()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        for (int i = 0; i < 10; i++)
            HandleMouse(form, Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.Equal(3, form.ScrollTop);

        for (int i = 0; i < 10; i++)
            HandleMouse(form, Mouse(2, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, form.ScrollTop);
    }

    [Fact]
    public void PageDownAndPageUp_MoveFocusAndScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        HandleKey(form, Key(ConsoleKey.PageDown));
        Assert.True(form.FocusIndex >= 3);
        Assert.True(form.ScrollTop > 0);

        HandleKey(form, Key(ConsoleKey.PageUp));
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

        HandleMouse(form, Mouse(2, 1));

        Assert.Equal(1, form.FocusIndex);
    }

    [Fact]
    public void ClickCheckbox_ChangesValue()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one")) { Id = "option" };
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        var result = HandleMouse(form, Mouse(2, 0));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Equal("option", result.SourceRowId);
        Assert.True(checkbox.Value);
    }

    [Fact]
    public void Render_InterruptedAttemptPublishesOnlyFinalFormLayout()
    {
        var driver = new FakeConsoleDriver(20, 5)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = static currentDriver => currentDriver.SetSize(20, 8),
        };
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        int renderAttempts = 0;
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context =>
        {
            renderAttempts++;
            int y = context.Size.Height - 1;
            var bounds = new Rect(0, y, context.Size.Width, 1);
            return new FormRenderContext(context, bounds);
        });
        var host = UiTestHost.Create(screen, layer);

        host.Composition.Render();

        Assert.Equal(2, renderAttempts);
        Assert.Equal(8, host.Composition.LastStableViewport?.Height);
        host.Composition.DispatchInput(Mouse(2, 4));
        Assert.Equal(FormInputResultKind.NotHandled, layer.LastRouteResult?.FormResult.Kind);
        Assert.False(checkbox.Value);
        host.Composition.DispatchInput(Mouse(2, 7));
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult?.FormResult.Kind);
        Assert.True(checkbox.Value);
    }

    [Fact]
    public void ClickChoiceSegment_SelectsItem()
    {
        var choice = new ChoiceFormRow<string>(new ChoiceModel<string>(["one", "two"], static value => value), string.Empty);
        var form = new ScrollableFormDialog([choice]);
        var driver = Render(form, visibleRows: 1);
        int x = driver.GetRow(0).IndexOf("two", StringComparison.Ordinal);

        HandleMouse(form, Mouse(x, 0));

        Assert.Equal("two", choice.Value);
    }

    [Fact]
    public void ClickNonFocusableRow_DoesNotMoveFocus()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")),
            new LabelRow("label", DialogStyles.Fill),
        ]);
        Render(form, visibleRows: 2);

        HandleMouse(form, Mouse(2, 1));

        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void ClickOutsideBody_IsIgnored()
    {
        var form = new ScrollableFormDialog([new CheckBoxRow(new CheckBoxLine("one"))]);
        Render(form, visibleRows: 1);

        var result = HandleMouse(form, Mouse(2, 3));

        Assert.Equal(FormInputResultKind.NotHandled, result.Kind);
        Assert.Equal(0, form.FocusIndex);
    }

    [Fact]
    public void RightClick_IsIgnoredByRows()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        HandleMouse(form, Mouse(2, 0, MouseButton.Right));

        Assert.False(checkbox.Value);
    }

    [Fact]
    public void ScrollbarClick_ChangesScroll()
    {
        var form = LongForm();
        Render(form, visibleRows: 3);

        HandleMouse(form, Mouse(19, 2));

        Assert.True(form.ScrollTop > 0);
    }

    [Fact]
    public void KeyDispatch_GoesToFocusedRow()
    {
        var checkbox = new CheckBoxRow(new CheckBoxLine("one")) { Id = "option" };
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        FormInputResult result = HandleKey(form, Key(ConsoleKey.Spacebar));

        Assert.Equal("option", result.SourceRowId);
        Assert.True(checkbox.Value);
    }

    [Fact]
    public void FormNavigation_HasNoSourceRowId()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("two")) { Id = "second" },
        ]);
        Render(form, visibleRows: 2);

        FormInputResult result = HandleKey(form, Key(ConsoleKey.Tab));

        Assert.Equal(FormInputResultKind.Handled, result.Kind);
        Assert.Null(result.SourceRowId);
    }

}
