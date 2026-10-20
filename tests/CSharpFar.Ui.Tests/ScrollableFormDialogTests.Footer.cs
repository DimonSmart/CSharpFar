using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void ButtonRow_ReturnsSubmitAndCancel()
    {
        var form = new ScrollableFormDialog([
            new ButtonRow(
                [
                    new DialogButton("ok", "OK", 'O', IsDefault: true),
                    new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
                ]),
        ]);
        Render(form, visibleRows: 1);

        Assert.Equal(FormInputResultKind.Submit, HandleKey(form, Key(ConsoleKey.Enter)).Kind);

        HandleKey(form, Key(ConsoleKey.RightArrow));
        Assert.Equal(FormInputResultKind.Cancel, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
    }

    [Fact]
    public void ButtonRow_DoesNotInferCancelRoleFromButtonId()
    {
        var form = new ScrollableFormDialog([
            new ButtonRow([new DialogButton("cancel", "Keep working", 'K')]),
        ]);
        Render(form, visibleRows: 1);

        FormInputResult result = HandleKey(form, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("cancel", result.Command);
    }

    [Fact]
    public void Tab_MovesFromLastBodyRowToFooterRow()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "lastBody");

        FormInputResult result = HandleKey(form, Key(ConsoleKey.Tab));

        Assert.Equal(FormInputResultKind.Handled, result.Kind);
        Assert.Equal("footerButtons", form.FocusedRowId);
    }

    [Fact]
    public void ShiftTab_MovesFromFooterRowToLastBodyRow()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "footerButtons");

        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));

        Assert.Equal("lastBody", form.FocusedRowId);
    }

    [Fact]
    public void Tab_FromFooterWrapsToFirstBodyRow()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "footerButtons");

        HandleKey(form, Key(ConsoleKey.Tab));

        Assert.Equal("first", form.FocusedRowId);
    }

    [Fact]
    public void ShiftTab_FromFirstBodyWrapsToFooter()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);

        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));

        Assert.Equal("footerButtons", form.FocusedRowId);
    }

    [Fact]
    public void FooterButton_EnterReturnsSubmit()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "footerButtons");

        FormInputResult result = HandleKey(form, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("submit", result.Command);
        Assert.Equal("footerButtons", result.SourceRowId);
    }

    [Fact]
    public void FooterButton_CancelHotkeyReturnsCancel()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "footerButtons");

        FormInputResult result = HandleKey(form, new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false));

        Assert.Equal(FormInputResultKind.Cancel, result.Kind);
        Assert.Equal("cancel", result.Command);
    }

    [Fact]
    public void MouseClickFooterButton_FocusesFooterAndSubmits()
    {
        ScrollableFormDialog form = FooterForm();
        FakeConsoleDriver driver = RenderWithFooter(form, bodyRows: 2, footerY: 3);
        int submitX = driver.GetRow(3).IndexOf("Submit", StringComparison.Ordinal);

        Assert.Equal(FormInputResultKind.Handled, HandleMouse(form, Mouse(submitX, 3)).Kind);
        FormInputResult result = HandleMouse(form, Mouse(submitX, 3, kind: MouseEventKind.Up));

        Assert.Equal("footerButtons", form.FocusedRowId);
        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("footerButtons", result.SourceRowId);
    }

    [Fact]
    public void MouseClickBodyAfterFooterFocus_ReturnsFocusToBody()
    {
        ScrollableFormDialog form = FooterForm();
        RenderWithFooter(form, bodyRows: 2, footerY: 3);
        RequestFocus(form, "footerButtons");

        HandleMouse(form, Mouse(2, 1));

        Assert.Equal("lastBody", form.FocusedRowId);
    }

    [Fact]
    public void FooterDoesNotAffectBodyScrollTop()
    {
        var footer = FooterButtons();
        var form = LongForm();
        form.SetRows(
            [
                new CheckBoxRow(new CheckBoxLine("one")),
                new CheckBoxRow(new CheckBoxLine("two")),
                new CheckBoxRow(new CheckBoxLine("three")),
                new CheckBoxRow(new CheckBoxLine("four")),
                new CheckBoxRow(new CheckBoxLine("five")),
                new CheckBoxRow(new CheckBoxLine("six")),
            ],
            [footer]);
        FakeConsoleDriver driver = RenderWithFooter(form, bodyRows: 2, footerY: 3);

        HandleMouse(form, Mouse(2, 0, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.Equal(3, form.ScrollTop);
        Assert.Contains("Submit", driver.GetRow(3), StringComparison.Ordinal);
    }

}
