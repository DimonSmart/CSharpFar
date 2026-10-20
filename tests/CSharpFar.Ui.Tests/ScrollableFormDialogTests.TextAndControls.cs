using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void TextInput_DoesNotSwallowFormNavigationKeys()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([
            new TextInputRow(text),
            new CheckBoxRow(new CheckBoxLine("next")),
        ]);
        Render(form, visibleRows: 2);

        HandleKey(form, Key(ConsoleKey.DownArrow));

        Assert.Equal(1, form.FocusIndex);
        Assert.Equal(string.Empty, text.Text);
    }

    [Fact]
    public void TextInput_EnterMovesFocusThroughEnabledRowsAndFooter()
    {
        var disabled = new CheckBoxRow(new CheckBoxLine("Disabled")) { Id = "disabled", Enabled = false };
        var form = new ScrollableFormDialog();
        form.SetRows(
            [
                new TextInputRow(new CommandLineState()) { Id = "name" },
                disabled,
                new TextInputRow(new CommandLineState()) { Id = "path" },
            ],
            [new ButtonRow([DialogButton.Default("ok", "OK", 'O')]) { Id = "actions" }]);
        RenderWithFooter(form, bodyRows: 3, footerY: 4);

        Assert.Equal(FormInputResultKind.MoveFocusNext, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
        Assert.Equal("path", form.FocusedRowId);
        Assert.Equal(FormInputResultKind.MoveFocusNext, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
        Assert.Equal("actions", form.FocusedRowId);
    }

    [Fact]
    public void TextInput_SubmitOnEnterDoesNotMoveFocus()
    {
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState()) { Id = "search", SubmitOnEnter = true },
            new CheckBoxRow(new CheckBoxLine("Next")) { Id = "next" },
        ]);
        Render(form, visibleRows: 2);

        Assert.Equal(FormInputResultKind.NotHandled, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
        Assert.Equal("search", form.FocusedRowId);
        Assert.True(form.IsFocusedOnSubmitRow);
    }

    [Fact]
    public void TextInput_OpenHistoryConsumesEnterBeforeFocusTraversal()
    {
        var history = HistoryWithItems(3);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("Next")) { Id = "next" },
        ]);
        Render(form, visibleRows: 2);

        Assert.Equal(FormInputResultKind.OverlayChanged, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
        Assert.Equal("pattern", form.FocusedRowId);
    }

    [Fact]
    public void TextInput_EditKeyChangesValue()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([new TextInputRow(text)]);
        Render(form, visibleRows: 1);

        var result = HandleKey(form, new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Equal("a", text.Text);
    }

    [Fact]
    public void LabeledTextInput_EditKeyChangesValueWithSameResult()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Value:", text)]);
        Render(form, visibleRows: 1);

        var result = HandleKey(form, new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Equal("a", text.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextRows_HaveCompactKeyboardParity(bool labeled)
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([CreateTextRow(labeled, text)]);
        Render(form, visibleRows: 1);

        FormInputResult printable = HandleKey(form, new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        int afterPrintableCursor = text.CursorPosition;
        FormInputResult left = HandleKey(form, Key(ConsoleKey.LeftArrow));
        string afterLeftText = text.Text;
        HandleKey(form, Key(ConsoleKey.RightArrow));
        FormInputResult backspace = HandleKey(form, Key(ConsoleKey.Backspace));
        HandleKey(form, new ConsoleKeyInfo('b', ConsoleKey.B, shift: false, alt: false, control: false));
        HandleKey(form, new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false));
        HandleKey(form, new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        FormInputResult replace = HandleKey(form, new ConsoleKeyInfo('z', ConsoleKey.Z, shift: false, alt: false, control: false));

        Assert.Equal(FormInputResultKind.ValueChanged, printable.Kind);
        Assert.Equal(1, afterPrintableCursor);
        Assert.Equal(FormInputResultKind.Handled, left.Kind);
        Assert.Equal("a", afterLeftText);
        Assert.Equal(FormInputResultKind.ValueChanged, backspace.Kind);
        Assert.Equal(FormInputResultKind.ValueChanged, replace.Kind);
        Assert.Equal("z", text.Text);
        Assert.Equal(1, text.CursorPosition);
    }

    [Theory]
    [InlineData(false, ConsoleKey.DownArrow)]
    [InlineData(true, ConsoleKey.DownArrow)]
    [InlineData(false, ConsoleKey.UpArrow)]
    [InlineData(true, ConsoleKey.UpArrow)]
    public void TextRows_HistoryEnterHasPriorityOverSubmitOnEnter(bool labeled, ConsoleKey selectionKey)
    {
        var history = HistoryWithItems(3);
        var text = new CommandLineState();
        var row = CreateTextRow(labeled, text, history, submitOnEnter: true);
        var form = new ScrollableFormDialog([row]);
        var driver = new FakeConsoleDriver(20, 8);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(selectionKey)));
        UiInputResult enter = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));

        Assert.True(enter.Handled);
        if (selectionKey == ConsoleKey.UpArrow)
        {
            Assert.Equal(FormInputResultKind.Submit, layer.LastRouteResult!.Value.FormResult.Kind);
            Assert.Equal(string.Empty, text.Text);
        }
        else
        {
            Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
            Assert.Equal("item-02", text.Text);
        }
        Assert.False(history.IsDropdownOpen);
        Assert.False(FormDialogInput.ShouldImplicitlySubmit(
            new UiRoutedInput<ScrollableFormFrame>(
                new KeyConsoleInputEvent(Key(ConsoleKey.Enter)),
                layer.CommittedFrame,
                FormTargetIds.ForExplicitRow("pattern"),
                UiInputRouteKind.FocusedTarget),
            layer.LastRouteResult.Value.FormResult,
            form));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextRows_HistoryEscapeHasPriorityOverFormCancel(bool labeled)
    {
        var history = HistoryWithItems(3);
        var row = CreateTextRow(labeled, new CommandLineState(), history, submitOnEnter: true);
        var form = new ScrollableFormDialog([row]);
        var driver = new FakeConsoleDriver(20, 8);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();

        UiInputResult escape = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.True(escape.Handled);
        Assert.False(history.IsDropdownOpen);
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.NotEqual(FormInputResultKind.Cancel, layer.LastRouteResult.Value.FormResult.Kind);
        Assert.Equal("pattern", form.FocusedRowId);

        UiInputResult secondEscape = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.True(secondEscape.Handled);
        Assert.Equal(FormInputResultKind.Cancel, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextRows_MouseClickUsesHorizontalViewportForCursorPosition(bool labeled)
    {
        var text = new CommandLineState();
        text.SetText("abcdefghijklmnopqrstuvwxyz");
        var row = CreateTextRow(labeled, text, width: 8);
        var form = new ScrollableFormDialog([row]);
        var driver = new FakeConsoleDriver(20, 5);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame target = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row);

        host.Composition.DispatchInput(Mouse(Assert.IsType<FormRowTargetFrame>(target).Layout.ControlBounds.X + 2, target.Bounds.Y));

        Assert.Equal(21, text.CursorPosition);
    }

    [Fact]
    public void LabeledTextInputRow_MaskedInputHidesRenderedValueButPreservesBuffer()
    {
        var text = new CommandLineState();
        text.SetText("secret");
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Password:", text, inputWidth: 10, maskInput: true)]);
        var driver = Render(form, visibleRows: 1);

        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("******", StringComparison.Ordinal));
        Assert.Equal(FormInputResultKind.ValueChanged, HandleKey(form, new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false)).Kind);
        Assert.Equal("secretx", text.Text);
    }

    [Fact]
    public void LabeledTextInputRow_PreservesHistoryScrollbarDragAcrossRecreation()
    {
        var text = new CommandLineState();
        var history = TextFieldHistoryTestProvider.CreateState();
        for (int i = 0; i < 20; i++) history.History.Add("item-" + i);
        Assert.True(history.OpenAll(5));
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Value:", text, history, inputWidth: 14)]);
        Render(form, visibleRows: 1, screenHeight: 8);

        Assert.True(Harnesses.TryGetValue(form, out FormHarness? harness));
        FormTargetFrame scrollbar = Assert.Single(harness.Layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);
        HandleMouse(form, Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.Left, MouseEventKind.Down));
        Assert.True(history.IsDropdownOpen);
        form.SetRows([new LabeledTextInputRow("Value:", text, history, inputWidth: 14)]);
        HandleMouse(form, Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 1, MouseButton.Left, MouseEventKind.Move));
        HandleMouse(form, Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 1, MouseButton.Left, MouseEventKind.Up));

        Assert.True(history.IsDropdownOpen);
    }

    [Fact]
    public void LabeledValueRow_IsReadOnlyDiagnosticTarget()
    {
        var row = new LabeledValueRow("Value:", () => "very-long-value") { Id = "value" };
        var form = new ScrollableFormDialog([row, new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" }]);
        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 2);
        UiInteractionFrame interaction = form.BuildInteractionFrame(frame);

        Assert.False(row.IsFocusable);
        Assert.DoesNotContain(interaction.Focus.Entries, entry => entry.Target == FormTargetIds.ForExplicitRow("value"));
        Assert.Contains(frame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("value"));
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(false)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleMouse(Mouse(0, 0, MouseButton.Left, MouseEventKind.Down), new FormRowMouseContext(false, new FormRowLayout(new Rect(0, 0, 5, 1), null, new Rect(0, 0, 5, 1)))).Kind);
    }

    [Fact]
    public void LabeledValueRow_RendersLabelValueEmptyAndNullWithinBounds()
    {
        var longValue = new LabeledValueRow("Label:", () => "very-long-value") { Id = "long" };
        var emptyValue = new LabeledValueRow("Empty:", () => string.Empty) { Id = "empty" };
        var nullValue = new LabeledValueRow("Null:", () => null!) { Id = "null" };
        var form = new ScrollableFormDialog([longValue, emptyValue, nullValue]);
        var driver = Render(form, visibleRows: 3);

        Assert.Equal("Label: very-long-val", driver.GetRow(0));
        Assert.Equal("Empty:              ", driver.GetRow(1));
        Assert.Equal("Null:               ", driver.GetRow(2));
        Assert.DoesNotContain(driver.WriteRecords, record => record.X + record.Text.Length > 20);
    }

    [Fact]
    public void RoutedLabeledValueRow_ClickDoesNotStealFocusOrToggleNeighbor()
    {
        var value = new LabeledValueRow("Value:", () => "read-only") { Id = "value" };
        var checkbox = new CheckBoxRow(new CheckBoxLine("check")) { Id = "check" };
        var form = new ScrollableFormDialog([checkbox, value]);
        var driver = new FakeConsoleDriver(20, 5);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        RequestFocus(form, "check");
        (int cursorX, int cursorY) = (driver.CursorX, driver.CursorY);

        UiInputResult click = host.Composition.DispatchInput(Mouse(2, 1));
        host.Composition.Render();

        Assert.False(click.Handled);
        Assert.Equal("check", form.FocusedRowId);
        Assert.NotEqual(FormTargetIds.ForExplicitRow("value"), layer.FocusState.FocusedTarget);
        Assert.False(checkbox.Value);
        Assert.True(driver.CursorVisible);
        Assert.Equal((cursorX, cursorY), (driver.CursorX, driver.CursorY));
    }

    [Fact]
    public void DisabledCheckBox_IsExcludedFromFocusHitAndInput()
    {
        var disabled = new CheckBoxRow(new CheckBoxLine("disabled")) { Id = "disabled", Enabled = false };
        var form = new ScrollableFormDialog([disabled, new CheckBoxRow(new CheckBoxLine("enabled")) { Id = "enabled" }]);
        var driver = new FakeConsoleDriver(20, 5);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 2), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        ScrollableFormFrame frame = layer.CommittedFrame;
        UiInteractionFrame interaction = form.BuildInteractionFrame(frame);

        Assert.Equal("enabled", form.FocusedRowId);
        Assert.DoesNotContain(interaction.Focus.Entries, entry => entry.Target == FormTargetIds.ForExplicitRow("disabled"));
        Assert.DoesNotContain(interaction.HitRegions, region => region.Target == FormTargetIds.ForExplicitRow("disabled"));
        Assert.DoesNotContain(frame.Targets, target => target is FormRowTargetFrame { Target: var id, Cursor: not null } && id == FormTargetIds.ForExplicitRow("disabled"));
        Assert.Equal(FormInputResultKind.NotHandled, disabled.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(false)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, disabled.HandleKey(Key(ConsoleKey.Enter), new FormRowInputContext(false)).Kind);
        host.Composition.DispatchInput(Mouse(2, 0));
        Assert.False(disabled.Value);
    }

    [Fact]
    public void ScrollableFormDialog_DisablingFocusedRowMovesFocusToEnabledTarget()
    {
        var first = new CheckBoxRow(new CheckBoxLine("first")) { Id = "first" };
        var second = new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" };
        var form = new ScrollableFormDialog([first, second]);
        var driver = new FakeConsoleDriver(20, 5);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 2);
        host.Composition.Render();

        RequestFocus(form, "first");
        first.Enabled = false;
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));

        Assert.NotEqual("first", form.FocusedRowId);
        Assert.Equal("second", form.FocusedRowId);
        Assert.True(second.Value);
    }

    [Fact]
    public void ScrollableFormDialog_ReenabledRowReturnsToInteractionFrame()
    {
        var row = new CheckBoxRow(new CheckBoxLine("target")) { Id = "target", Enabled = false };
        var other = new CheckBoxRow(new CheckBoxLine("other")) { Id = "other" };
        var form = new ScrollableFormDialog([row, other]);
        var driver = new FakeConsoleDriver(20, 5);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();

        row.Enabled = true;
        host.Composition.Render();
        UiTargetId target = FormTargetIds.ForExplicitRow("target");

        Assert.Contains(layer.CommittedInteractionFrame.Focus.Entries, entry => entry.Target == target);
        Assert.Contains(layer.CommittedInteractionFrame.HitRegions, region => region.Target == target);
        RequestFocus(form, "target");
        Assert.Contains(layer.CommittedFrame.Targets, frame => frame is FormRowTargetFrame { Target: var id, Cursor: not null } && id == target);
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));
        Assert.True(row.Value);
        host.Composition.DispatchInput(Mouse(2, 0));
        Assert.False(row.Value);
    }

    [Fact]
    public void CursorPlacement_FollowsFocusedTextCheckboxAndChoiceRows()
    {
        var text = new CommandLineState();
        text.SetText("abc");
        var form = new ScrollableFormDialog([
            new TextInputRow(text),
            new CheckBoxRow(new CheckBoxLine("check")),
            new ChoiceFormRow<string>(new ChoiceModel<string>(["one", "two"], static value => value), string.Empty),
        ]);

        var driver = Render(form, visibleRows: 3);
        Assert.True(driver.CursorVisible);
        Assert.Equal((3, 0), (driver.CursorX, driver.CursorY));

        HandleKey(form, Key(ConsoleKey.Tab));
        Render(form, driver, visibleRows: 3);
        Assert.Equal((1, 1), (driver.CursorX, driver.CursorY));

        HandleKey(form, Key(ConsoleKey.Tab));
        Render(form, driver, visibleRows: 3);
        Assert.Equal((1, 2), (driver.CursorX, driver.CursorY));

        HandleKey(form, Key(ConsoleKey.RightArrow));
        Render(form, driver, visibleRows: 3);
        Assert.Equal((9, 2), (driver.CursorX, driver.CursorY));
    }

    [Fact]
    public void FocusedRowWithoutCursorProvider_HidesCursor()
    {
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState()),
            new ButtonRow([new DialogButton("ok", "OK", 'O')]),
        ]);
        var driver = Render(form, visibleRows: 2);
        Assert.True(driver.CursorVisible);

        HandleKey(form, Key(ConsoleKey.Tab));
        Render(form, driver, visibleRows: 2);

        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void MultiLineChoice_IsOneFocusableControlAndCursorFollowsSecondLineSelection()
    {
        var choice = new ChoiceModel<string>(["one", "two", "three", "four"], static value => value, selectedIndex: 2);
        var multiLine = new MultiLineChoiceFormRow<string>(choice, string.Empty, [2, 4]);
        var form = new ScrollableFormDialog([
            multiLine,
            new CheckBoxRow(new CheckBoxLine("next")),
        ]);

        var driver = Render(form, visibleRows: 3);

        Assert.Equal(2, multiLine.Height);
        Assert.Equal(2, form.FocusableCount);
        Assert.Equal((1, 1), (driver.CursorX, driver.CursorY));

        HandleKey(form, Key(ConsoleKey.RightArrow));
        Render(form, driver, visibleRows: 3);
        Assert.Equal((11, 1), (driver.CursorX, driver.CursorY));

        HandleKey(form, Key(ConsoleKey.Tab));
        Assert.Equal(1, form.FocusIndex);
    }

    [Fact]
    public void TextInputRow_PreservesHistoryScrollbarDragAcrossRowRecreation()
    {
        var text = new CommandLineState();
        var history = TextFieldHistoryTestProvider.CreateState();
        for (int i = 0; i < 20; i++)
            history.History.Add("item-" + i);
        Assert.True(history.OpenAll(availableContentRows: 5));

        var form = new ScrollableFormDialog([new TextInputRow(text, history)]);
        Render(form, visibleRows: 1, screenHeight: 8);

        HandleMouse(form, Mouse(19, 3, MouseButton.Left, MouseEventKind.Down));
        Assert.NotNull(history.Scrollbar.DragState);

        form.SetRows([new TextInputRow(text, history)]);
        HandleMouse(form, Mouse(19, 5, MouseButton.Left, MouseEventKind.Move));
        HandleMouse(form, Mouse(19, 5, MouseButton.Left, MouseEventKind.Up));

        Assert.Null(history.Scrollbar.DragState);
        Assert.True(history.FirstVisibleIndex > 0);
    }

}
