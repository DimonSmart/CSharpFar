using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ScrollableFormDialogTests
{
    private static readonly ConditionalWeakTable<ScrollableFormDialog, FormHarness> Harnesses = new();

    [Fact]
    public void FocusedRowId_ReturnsCurrentFocusableRowId()
    {
        var form = new ScrollableFormDialog([
            new LabelRow("label", FarDialogStyles.Fill),
            new TextInputRow(new CommandLineState()) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" },
        ]);
        Render(form, visibleRows: 3);

        Assert.Equal("first", form.FocusedRowId);

        HandleKey(form, Key(ConsoleKey.Tab));

        Assert.Equal("second", form.FocusedRowId);
    }

    [Fact]
    public void FocusRequest_MovesFocusToRowByIdOnCommit()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("first")) { Id = "first" },
            new LabelRow("label", FarDialogStyles.Fill),
            new CheckBoxRow(new CheckBoxLine("target")) { Id = "target" },
        ]);
        Render(form, visibleRows: 1);

        RequestFocus(form, "target");

        Assert.Equal("target", form.FocusedRowId);
        Assert.Equal(2, form.ScrollTop);
    }

    [Fact]
    public void InitialFocusBeforeFirstRender_MakesTargetVisible()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")) { Id = "one" },
            new CheckBoxRow(new CheckBoxLine("two")) { Id = "two" },
            new CheckBoxRow(new CheckBoxLine("three")) { Id = "three" },
            new CheckBoxRow(new CheckBoxLine("four")) { Id = "four" },
            new TextInputRow(new CommandLineState()) { Id = "last" },
        ]);
        form.SetInitialFocus("last");

        var driver = new FakeConsoleDriver(20, 6);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), FarDialogStyles.Border));
        UiTestHost.Create(screen, layer).Composition.Render();

        FormTargetFrame target = Assert.Single(layer.CommittedFrame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("last"));
        Assert.Equal("last", form.FocusedRowId);
        Assert.Equal(3, form.ScrollTop);
        Assert.NotNull(target.HitBounds);
        Assert.True(driver.CursorVisible);
    }

    [Fact]
    public void InitialFocusBeforeFirstRender_SurvivesRejectedRenderAttempt()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("one")) { Id = "one" },
            new CheckBoxRow(new CheckBoxLine("two")) { Id = "two" },
            new CheckBoxRow(new CheckBoxLine("three")) { Id = "three" },
            new CheckBoxRow(new CheckBoxLine("four")) { Id = "four" },
            new TextInputRow(new CommandLineState()) { Id = "last" },
        ]);
        form.SetInitialFocus("last");
        var driver = new FakeConsoleDriver(20, 6)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = current => current.SetSize(21, 6),
        };
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), FarDialogStyles.Border));

        UiTestHost.Create(screen, layer).Composition.Render();

        Assert.Equal("last", form.FocusedRowId);
        Assert.Equal(3, form.ScrollTop);
        Assert.NotNull(Assert.Single(layer.CommittedFrame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("last")).HitBounds);
    }

    [Fact]
    public void SetInitialFocus_ThrowsForMissingId()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("first")) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" },
        ]);
        Assert.Throws<ArgumentException>(() => form.SetInitialFocus("missing"));
    }

    [Fact]
    public void IsFocusedOnSubmitRow_UsesSubmitOnEnter()
    {
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState()) { Id = "search", SubmitOnEnter = true },
            new CheckBoxRow(new CheckBoxLine("option")) { Id = "option" },
        ]);
        Render(form, visibleRows: 2);

        Assert.True(form.IsFocusedOnSubmitRow);

        HandleKey(form, Key(ConsoleKey.Tab));

        Assert.False(form.IsFocusedOnSubmitRow);
    }

    [Fact]
    public void SetRows_DuplicateFocusableIds_Throws()
    {
        var form = new ScrollableFormDialog();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => form.SetRows([
            new TextInputRow(new CommandLineState()) { Id = "duplicate" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "duplicate" },
        ]));

        Assert.Contains("duplicate", exception.Message);
    }

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
    public void RenderFrame_PublishesFocusEntriesHitRegionsAndScrollbarTarget()
    {
        var form = new ScrollableFormDialog([
            new LabelRow("label", FarDialogStyles.Fill) { Id = "label" },
            new TextInputRow(new CommandLineState()) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" },
            new CheckBoxRow(new CheckBoxLine("third")) { Id = "third" },
        ]);

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 2);
        UiInteractionFrame interaction = form.BuildInteractionFrame(frame);

        Assert.Contains(frame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("first") && target.IsFocusable);
        Assert.Contains(frame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("label") && !target.IsFocusable);
        Assert.Contains(frame.Targets, target => target is { Kind: FormTargetKind.BodyScrollbar, Target: var id } && id == FormTargetIds.BodyScrollbar);
        Assert.Equal(3, interaction.Focus.Entries.Count);
        Assert.Contains(interaction.HitRegions, region => region.Target == FormTargetIds.BodyScrollbar);
    }

    [Fact]
    public void RenderFrame_PublishesHistoryDropdownTargetsForFocusedRow()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("alpha");
        history.Add("beta");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
        ]);
        Assert.True(SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY: 0, screenHeight: 8));

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 1, screenHeight: 8);

        Assert.Contains(frame.Targets, target => target is { Kind: FormTargetKind.HistoryDropdown, Target: var id } && id == FormTargetIds.ForHistoryDropdown(FormTargetIds.ForExplicitRow("pattern")));
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
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        var result = HandleMouse(form, Mouse(2, 0));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
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
        var choice = new ChoiceFormRow<string>(new ChoiceRow<string>(["one", "two"], static value => value), string.Empty);
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
            new LabelRow("label", FarDialogStyles.Fill),
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
        var checkbox = new CheckBoxRow(new CheckBoxLine("one"));
        var form = new ScrollableFormDialog([checkbox]);
        Render(form, visibleRows: 1);

        HandleKey(form, Key(ConsoleKey.Spacebar));

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

        HandleKey(form, Key(ConsoleKey.DownArrow));

        Assert.Equal(1, form.FocusIndex);
        Assert.Equal(string.Empty, text.Text);
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
        int expectedIndex = selectionKey == ConsoleKey.DownArrow ? 1 : history.Matches.Count - 1;

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(selectionKey)));
        UiInputResult enter = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));

        Assert.True(enter.Handled);
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal($"item-{expectedIndex:D2}", text.Text);
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

        host.Composition.DispatchInput(Mouse(target.Bounds.X + 2, target.Bounds.Y));

        Assert.Equal(21, text.CursorPosition);
    }

    [Fact]
    public void LabeledTextInputRow_MaskedInputHidesRenderedValueButPreservesBuffer()
    {
        var text = new CommandLineState();
        text.SetText("secret");
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Password:", text, labelWidth: 0, inputWidth: 10, maskInput: true)]);
        var driver = Render(form, visibleRows: 1);

        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("******", StringComparison.Ordinal));
        Assert.Equal(FormInputResultKind.ValueChanged, HandleKey(form, new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false)).Kind);
        Assert.Equal("secretx", text.Text);
    }

    [Fact]
    public void LabeledTextInputRow_StatePreservesHistoryScrollbarDragAcrossRecreation()
    {
        var text = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < 20; i++) history.Add("item-" + i);
        Assert.True(history.OpenAll(5));
        var state = new TextInputRowState();
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Value:", text, history, state, labelWidth: 0, inputWidth: 14)]);
        Render(form, visibleRows: 1, screenHeight: 8);

        HandleMouse(form, Mouse(13, 3, MouseButton.Left, MouseEventKind.Down));
        Assert.NotNull(state.HistoryScrollbarDrag);
        form.SetRows([new LabeledTextInputRow("Value:", text, history, state, labelWidth: 0, inputWidth: 14)]);
        HandleMouse(form, Mouse(13, 5, MouseButton.Left, MouseEventKind.Move));
        HandleMouse(form, Mouse(13, 5, MouseButton.Left, MouseEventKind.Up));

        Assert.Null(state.HistoryScrollbarDrag);
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
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(0, false)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleMouse(Mouse(0, 0, MouseButton.Left, MouseEventKind.Down), new FormRowMouseContext(new Rect(0, 0, 5, 1), 0, false, 5)).Kind);
    }

    [Fact]
    public void LabeledValueRow_RendersLabelValueEmptyAndNullWithinBounds()
    {
        var longValue = new LabeledValueRow("Label:", () => "very-long-value", labelWidth: 7) { Id = "long" };
        var emptyValue = new LabeledValueRow("Empty:", () => string.Empty, labelWidth: 7) { Id = "empty" };
        var nullValue = new LabeledValueRow("Null:", () => null!, labelWidth: 7) { Id = "null" };
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
        Assert.NotEqual(FormTargetIds.ForExplicitRow("value"), layer.FocusScope.FocusedTarget);
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
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 2), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        ScrollableFormFrame frame = layer.CommittedFrame;
        UiInteractionFrame interaction = form.BuildInteractionFrame(frame);

        Assert.Equal("enabled", form.FocusedRowId);
        Assert.DoesNotContain(interaction.Focus.Entries, entry => entry.Target == FormTargetIds.ForExplicitRow("disabled"));
        Assert.DoesNotContain(interaction.HitRegions, region => region.Target == FormTargetIds.ForExplicitRow("disabled"));
        Assert.DoesNotContain(frame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("disabled") && target.Cursor is not null);
        Assert.Equal(FormInputResultKind.NotHandled, disabled.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(0, false)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, disabled.HandleKey(Key(ConsoleKey.Enter), new FormRowInputContext(0, false)).Kind);
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
        Assert.Contains(layer.CommittedFrame.Targets, frame => frame.Target == target && frame.Cursor is not null);
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
            new ChoiceFormRow<string>(new ChoiceRow<string>(["one", "two"], static value => value), string.Empty),
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
            new ButtonRow([new DialogButton("ok", "OK", 'O')], FarDialogStyles.Fill, FarDialogStyles.FocusedInput),
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
        var choice = new ChoiceRow<string>(["one", "two", "three", "four"], static value => value, selectedIndex: 2);
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

        HandleMouse(form, Mouse(19, 3, MouseButton.Left, MouseEventKind.Down));
        Assert.NotNull(state.HistoryScrollbarDrag);

        form.SetRows([new TextInputRow(text, history, state)]);
        HandleMouse(form, Mouse(19, 5, MouseButton.Left, MouseEventKind.Move));
        HandleMouse(form, Mouse(19, 5, MouseButton.Left, MouseEventKind.Up));

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

        Assert.Equal(FormInputResultKind.Submit, HandleKey(form, Key(ConsoleKey.Enter)).Kind);

        HandleKey(form, Key(ConsoleKey.RightArrow));
        Assert.Equal(FormInputResultKind.Cancel, HandleKey(form, Key(ConsoleKey.Enter)).Kind);
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

        FormInputResult result = HandleMouse(form, Mouse(submitX, 3));

        Assert.Equal("footerButtons", form.FocusedRowId);
        Assert.Equal(FormInputResultKind.Submit, result.Kind);
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

    [Fact]
    public void RoutedWheelScroll_PersistsAfterRenderWithoutMovingFocus()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([
            new TextInputRow(text) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("two")) { Id = "two" },
            new CheckBoxRow(new CheckBoxLine("three")) { Id = "three" },
            new CheckBoxRow(new CheckBoxLine("four")) { Id = "four" },
            new CheckBoxRow(new CheckBoxLine("five")) { Id = "five" },
        ]);
        var driver = new FakeConsoleDriver(20, 6);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 3);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.True(result.Invalidate);
        Assert.Equal(2, form.ScrollTop);
        Assert.Equal("first", form.FocusedRowId);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void RoutedWheelScroll_HidesOffscreenHistoryOverlayUntilFocusedRowReceivesKey()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("alpha");
        Assert.True(history.OpenAll(availableContentRows: 3));
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new LabelRow("two", FarDialogStyles.Fill),
            new LabelRow("three", FarDialogStyles.Fill),
            new LabelRow("four", FarDialogStyles.Fill),
            new LabelRow("five", FarDialogStyles.Fill),
        ]);
        var driver = new FakeConsoleDriver(20, 8);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();

        host.Composition.DispatchInput(Mouse(2, 0, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.Equal("pattern", form.FocusedRowId);
        Assert.Equal(3, form.ScrollTop);
        Assert.False(driver.CursorVisible);
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target => target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.HistoryScrollbar);
        Assert.DoesNotContain("alpha", driver.GetRow(0), StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", driver.GetRow(1), StringComparison.Ordinal);

        host.Composition.DispatchInput(new KeyConsoleInputEvent(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)));
        host.Composition.Render();

        Assert.Equal(0, form.ScrollTop);
        Assert.True(driver.CursorVisible);
        Assert.Contains(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);
        Assert.Contains("alpha", string.Concat(Enumerable.Range(0, 8).Select(driver.GetRow)), StringComparison.Ordinal);
    }

    [Fact]
    public void RoutedHistoryArrow_TogglesOpenDropdownClosed()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("alpha");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
        ]);
        var driver = new FakeConsoleDriver(20, 8);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 1);
        host.Composition.Render();

        UiInputResult open = host.Composition.DispatchInput(Mouse(19, 0));
        host.Composition.Render();
        UiInputResult close = host.Composition.DispatchInput(Mouse(19, 0));

        Assert.True(open.Invalidate);
        Assert.True(close.Invalidate);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedHistoryCloseOnLabelClick_InvalidatesFrame()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("alpha");
        Assert.True(SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY: 0, screenHeight: 8));
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new LabelRow("covered", FarDialogStyles.Fill) { Id = "covered" },
            new LabelRow("covered", FarDialogStyles.Fill),
            new LabelRow("covered", FarDialogStyles.Fill),
            new LabelRow("covered", FarDialogStyles.Fill),
            new LabelRow("label", FarDialogStyles.Fill) { Id = "label" },
        ]);
        var driver = new FakeConsoleDriver(20, 12);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 6);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 5));

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedWheelInsideHistoryPopup_MovesHistoryWithoutScrollingBodyOrChangingValue()
    {
        var text = new CommandLineState();
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(text, history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 4), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);

        UiInputResult down = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.True(down.Handled);
        Assert.True(down.Invalidate);
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);
        Assert.Equal(string.Empty, text.Text);
        Assert.True(history.IsDropdownOpen);
        Assert.Equal("pattern", form.FocusedRowId);

        UiInputResult up = host.Composition.DispatchInput(Mouse(popup.Bounds.X, popup.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));

        Assert.True(up.Handled);
        Assert.Equal(0, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        UiInputResult topBoundary = host.Composition.DispatchInput(Mouse(popup.Bounds.X, popup.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.True(topBoundary.Handled);
        Assert.Equal(0, history.SelectedIndex);
    }

    [Fact]
    public void RoutedWheelOnHistoryScrollbarAndAtBoundaries_BelongsToHistory()
    {
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 3), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        UiInputResult onScrollbar = host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.True(onScrollbar.Handled);
        Assert.Equal(1, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        for (int i = 0; i < history.Matches.Count; i++)
            host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));
        int last = history.SelectedIndex;
        UiInputResult boundary = host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(boundary.Handled);
        Assert.Equal(last, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);
        Assert.True(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedWheelOutsideHistoryPopup_ScrollsBodyWithoutChangingHistorySelection()
    {
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
            new CheckBoxRow(new CheckBoxLine("six")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 3);
        host.Composition.Render();

        host.Composition.DispatchInput(Mouse(2, 0, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(form.ScrollTop > 0);
        Assert.Equal(0, history.SelectedIndex);
    }

    [Fact]
    public void RoutedHistoryMouseResults_OnlyChangedBufferReportsValueChanged()
    {
        var history = HistoryWithItems(12);
        var text = new CommandLineState();
        text.SetText(history.Matches[0]);
        var form = new ScrollableFormDialog([
            new TextInputRow(text, history) { Id = "pattern" },
            new LabelRow("outside", FarDialogStyles.Fill),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 2), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 1));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);

        Assert.True(history.OpenAll(availableContentRows: 8));
        host.Composition.Render();
        popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);
        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);

        Assert.True(history.OpenAll(availableContentRows: 8));
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 2));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownFrame_IsPublishedOnPrimaryRowEvenWhenClosed()
    {
        var dropdown = new DropdownSelect<string>(["one", "two", "three"], static item => item);
        var row = new DropdownSelectFormRow<string>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([row]);

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 1, screenHeight: 8);

        FormTargetFrame target = Assert.Single(frame.Targets, target => target.Kind == FormTargetKind.Row);
        DropdownSelectFrame dropdownFrame = Assert.IsType<DropdownSelectFrame>(target.DropdownFrame);
        Assert.Equal(row.GetFieldBounds(target.Bounds), dropdownFrame.FieldBounds);
        Assert.Null(dropdownFrame.PopupBounds);
    }

    [Fact]
    public void DropdownKeyboard_UsesCommittedListState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        DropdownSelectFrame committed = Assert.IsType<DropdownSelectFrame>(
            Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row).DropdownFrame);
        Assert.Equal(0, committed.ListState.SelectedIndex);

        dropdown.SelectedIndex = 6;
        dropdown.ScrollTop = 6;
        UiInputResult result = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal(0, dropdown.ScrollTop);
    }

    [Fact]
    public void ClosedDropdownOpening_UsesCommittedListState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();

        dropdown.SelectedIndex = 6;
        dropdown.ScrollTop = 6;
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));

        Assert.True(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, dropdown.ScrollTop);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownOpen_AfterFailedRenderNextInputUsesCommittedClosedState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(FormInputResultKind.Cancel, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownClose_AfterFailedRenderNextInputUsesCommittedOpenState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        Assert.False(dropdown.IsOpen);
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownCancelBaseline_RestoresFromCommittedFrame()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();

        dropdown.SelectedIndex = 6;
        dropdown.Close(commit: true);
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownMouse_UsesCommittedPopupGeometry()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        driver.SetSize(24, 4);

        UiInputResult result = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownMouse_AfterFailedRenderUsesPreviousCommittedGeometry()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        layer.ThrowOnRender = true;

        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        UiInputResult result = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownFocusTransition_CancelsTemporarySelectionAndRemovesTargets()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Tab)));
        host.Composition.Render();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.HasScrollbarDrag);
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
    }

    [Fact]
    public void DropdownMouseFocusTransition_CancelsTemporarySelection()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();
        FormTargetFrame next = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row && target.Row?.Id == "next");

        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal("next", form.FocusedRowId);
    }

    [Fact]
    public void DropdownTargets_UsePopupBeforeUnderlyingRows()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("covered")) { Id = "covered" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        UiInteractionFrame interaction = form.BuildInteractionFrame(layer.CommittedFrame);

        Assert.True(interaction.TryHitTest(popup.Bounds.X + 1, popup.Bounds.Y + 1, out UiHitRegion hit));
        Assert.Equal(FormTargetIds.ForDropdownPopup(FormTargetIds.ForExplicitRow("choice")), hit.Target);
    }

    [Fact]
    public void DropdownMouseResults_OnlyConfirmedChangedValueReportsValueChanged()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame row = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row);

        host.Composition.DispatchInput(Mouse(row.DropdownFrame!.Value.FieldBounds.X, row.DropdownFrame.Value.FieldBounds.Y));
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);

        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 2));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);

        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_OutsideClickRestoresCommittedValueAndClosesOverlay()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([row]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());

        host.Composition.DispatchInput(Mouse(23, 9));

        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);

        layer.ThrowOnRender = false;
        host.Composition.Render();
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
        Assert.Equal(0, Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row).DropdownFrame!.Value.ListState.SelectedIndex);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_ClickOtherRowDoesNotPublishRejectedSelection()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
            row,
        ]);
        form.SetInitialFocus("choice");
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        FormTargetFrame next = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row && target.Row?.Id == "next");

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);

        layer.ThrowOnRender = false;
        host.Composition.Render();
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_FooterSubmitUsesCommittedValue()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog();
        form.SetRows([row], [FooterButtons()]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFooterFormHostWithLayer(form, driver, bodyRows: 1, footerY: 5);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        FormTargetFrame footer = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row && target.IsFooter);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(footer.Bounds.X + 2, footer.Bounds.Y));

        Assert.Equal(FormInputResultKind.Submit, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);
    }

    [Fact]
    public void DropdownClosedRejectedRender_OutsideClickRestoresCommittedOpenLifecycleThenCancels()
    {
        var dropdown = OpenDropdown();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(23, 9));

        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownOpenRejectedRender_ClickOtherRowIgnoresRejectedPopup()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
            row,
        ]);
        form.SetInitialFocus("choice");
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormTargetFrame next = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row && target.Row?.Id == "next");

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));
        Assert.True(dropdown.IsOpen);
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 4,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 12);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        host.Composition.DispatchInput(Mouse(0, 11, MouseButton.Left, MouseEventKind.Move));

        Assert.Equal(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
        Assert.Equal(scrollbar.Target, layer.LastRouteTarget);

        host.Composition.DispatchInput(Mouse(0, 11, MouseButton.Left, MouseEventKind.Up));
        host.Composition.DispatchInput(Mouse(0, 11, MouseButton.Left, MouseEventKind.Move));

        Assert.NotEqual(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
    }

    [Fact]
    public void DropdownScrollbarCapture_DisappearingTargetClearsCapture()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 12);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));

        dropdown.MaxVisibleRows = 8;
        host.Composition.Render();
        host.Composition.DispatchInput(Mouse(0, 11, MouseButton.Left, MouseEventKind.Move));

        Assert.DoesNotContain(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
    }

    [Fact]
    public void DropdownScrollbarDrag_CommittedResizeKeepsCaptureWithNewScrollbarTarget()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 4,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 12);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame oldScrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(oldScrollbar.Bounds.X, oldScrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));

        dropdown.MaxVisibleRows = 5;
        host.Composition.Render();
        FormTargetFrame newScrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(0, 8, MouseButton.Left, MouseEventKind.Move));

        Assert.NotEqual(oldScrollbar.Bounds, newScrollbar.Bounds);
        Assert.Equal(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
        Assert.Equal(newScrollbar.Target, layer.LastRouteTarget);
    }

    [Fact]
    public void RoutedScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var form = LongForm();
        var driver = new FakeConsoleDriver(20, 8);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 4), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.BodyScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        host.Composition.DispatchInput(Mouse(0, 7, MouseButton.Left, MouseEventKind.Move));
        host.Composition.DispatchInput(Mouse(0, 7, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = form.ScrollTop;
        UiInputResult next = host.Composition.DispatchInput(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(afterDrag > 0);
        Assert.True(next.Handled);
        Assert.True(form.ScrollTop >= afterDrag);
    }

    [Fact]
    public void RoutedHistoryScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var history = HistoryWithItems(20);
        var state = new TextInputRowState();
        var form = new ScrollableFormDialog([new TextInputRow(new CommandLineState(), history, state) { Id = "pattern" }]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 1), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Move));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = history.FirstVisibleIndex;
        UiInputResult next = host.Composition.DispatchInput(Mouse(2, 2));

        Assert.True(afterDrag > 0);
        Assert.Null(state.HistoryScrollbarDrag);
        Assert.True(next.Handled);
    }

    [Fact]
    public void RoutedLabeledHistoryScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var history = HistoryWithItems(20);
        var state = new TextInputRowState();
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Value:", new CommandLineState(), history, state, labelWidth: 0, inputWidth: 20) { Id = "pattern" }]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 1), FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        UiInputResult mouseDown = layer.LastRouteResult!.Value.UiResult;
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Move));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = history.FirstVisibleIndex;
        host.Composition.DispatchInput(Mouse(2, 2, MouseButton.Left, MouseEventKind.Move));

        Assert.Equal(UiMouseCaptureRequestKind.Capture, mouseDown.MouseCaptureRequest.Kind);
        Assert.Equal(scrollbar.Target, mouseDown.MouseCaptureRequest.Target);
        Assert.True(afterDrag > 0);
        Assert.Null(state.HistoryScrollbarDrag);
        Assert.Equal(afterDrag, history.FirstVisibleIndex);
    }

    [Fact]
    public void RoutedMouseFocus_NotHandledRowInvalidatesFrame()
    {
        var row = new NotHandledFocusableRow("target");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState()) { Id = "first" },
            row,
        ]);
        var driver = new FakeConsoleDriver(20, 5);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 2);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 1));
        host.Composition.Render();

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal("target", form.FocusedRowId);
        Assert.True(row.RenderedFocused);
    }

    [Fact]
    public void InitialFocus_FindsFooterRow()
    {
        ScrollableFormDialog form = FooterForm();

        form.SetInitialFocus("footerButtons");
        Assert.Equal("footerButtons", form.FocusedRowId);
        Assert.Equal(FormRowRole.ButtonBar, form.FocusedRowRole);
    }

    [Fact]
    public void DuplicateIdsAcrossBodyAndFooterThrow()
    {
        var form = new ScrollableFormDialog();

        Assert.Throws<InvalidOperationException>(() => form.SetRows(
            [new CheckBoxRow(new CheckBoxLine("body")) { Id = "same" }],
            [new ButtonRow([new DialogButton("ok", "OK", 'O')], FarDialogStyles.Fill, FarDialogStyles.FocusedInput) { Id = "same" }]));
    }

    [Fact]
    public void CursorHiddenWhenFooterButtonFocusedAndRestoredInBody()
    {
        ScrollableFormDialog form = FooterForm();
        FakeConsoleDriver driver = RenderWithFooter(form, bodyRows: 2, footerY: 3);
        Assert.True(driver.CursorVisible);
        RequestFocus(form, "footerButtons");
        Assert.False(driver.CursorVisible);

        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
        RenderWithFooter(form, driver, bodyRows: 2, footerY: 3);
        Assert.True(driver.CursorVisible);
        Assert.Equal("first", form.FocusedRowId);
    }

    private static ScrollableFormDialog FooterForm()
    {
        var form = new ScrollableFormDialog();
        form.SetRows(
            [
                new TextInputRow(new CommandLineState()) { Id = "first" },
                new CheckBoxRow(new CheckBoxLine("last")) { Id = "lastBody" },
            ],
            [FooterButtons()]);
        return form;
    }

    private static ButtonRow FooterButtons() =>
        new(
            [
                new DialogButton("submit", "Submit", 'S', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput)
        {
            Id = "footerButtons",
        };

    private static ScrollableFormDialog LongForm() =>
        new([
            new CheckBoxRow(new CheckBoxLine("one")),
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
            new CheckBoxRow(new CheckBoxLine("six")),
        ]);

    private static DropdownSelect<int> OpenDropdown(int itemCount = 8, int maxVisibleRows = 3)
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, itemCount).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = maxVisibleRows,
        };
        dropdown.Open();
        return dropdown;
    }

    private static SingleLineTextHistoryState HistoryWithItems(int count)
    {
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < count; i++)
            history.Add($"item-{i:D2}");
        Assert.True(history.OpenAll(availableContentRows: 8));
        return history;
    }

    private static FakeConsoleDriver Render(ScrollableFormDialog form, int visibleRows, int? screenHeight = null)
    {
        var driver = new FakeConsoleDriver(20, screenHeight ?? Math.Max(5, visibleRows + 2));
        Render(form, driver, visibleRows);
        return driver;
    }

    private static ScrollableFormFrame RenderFrame(ScrollableFormDialog form, int visibleRows, int? screenHeight = null)
    {
        var driver = new FakeConsoleDriver(20, screenHeight ?? Math.Max(5, visibleRows + 2));
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(
                context,
                new Rect(0, 0, 20, visibleRows),
                FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        RegisterHarness(form, host, layer);
        host.Composition.Render();
        return layer.CommittedFrame;
    }

    private static void Render(ScrollableFormDialog form, FakeConsoleDriver driver, int visibleRows)
    {
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(
                context,
                new Rect(0, 0, 20, visibleRows),
                FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        RegisterHarness(form, host, layer);
        host.Composition.Render();
    }

    private static FakeConsoleDriver RenderWithFooter(ScrollableFormDialog form, int bodyRows, int footerY)
    {
        var driver = new FakeConsoleDriver(20, Math.Max(6, footerY + 2));
        RenderWithFooter(form, driver, bodyRows, footerY);
        return driver;
    }

    private static void RenderWithFooter(ScrollableFormDialog form, FakeConsoleDriver driver, int bodyRows, int footerY)
    {
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(
                context,
                new Rect(0, 0, 20, bodyRows),
                FarDialogStyles.Border,
                new Rect(0, footerY, 20, 1)));
        var host = UiTestHost.Create(screen, layer);
        RegisterHarness(form, host, layer);
        host.Composition.Render();
    }

    private static UiTestHost CreateRoutedFormHost(ScrollableFormDialog form, FakeConsoleDriver driver, int visibleRows)
    {
        return CreateRoutedFormHostWithLayer(form, driver, visibleRows).Host;
    }

    private static (UiTestHost Host, TestFormLayer Layer) CreateRoutedFormHostWithLayer(ScrollableFormDialog form, FakeConsoleDriver driver, int visibleRows)
    {
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(
                context,
                new Rect(0, 0, 20, visibleRows),
                FarDialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        RegisterHarness(form, host, layer);
        return (host, layer);
    }

    private static (UiTestHost Host, TestFormLayer Layer) CreateRoutedFooterFormHostWithLayer(
        ScrollableFormDialog form,
        FakeConsoleDriver driver,
        int bodyRows,
        int footerY)
    {
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(
                context,
                new Rect(0, 0, 24, bodyRows),
                FarDialogStyles.Border,
                new Rect(0, footerY, 24, 1)));
        var host = UiTestHost.Create(screen, layer);
        RegisterHarness(form, host, layer);
        return (host, layer);
    }

    private static FormInputResult HandleKey(ScrollableFormDialog form, ConsoleKeyInfo key)
    {
        FormHarness harness = Harnesses.TryGetValue(form, out FormHarness? found)
            ? found
            : throw new InvalidOperationException("Render the form through a test UI layer before dispatching input.");
        harness.Host.Composition.DispatchInput(new KeyConsoleInputEvent(key));
        FormInputResult result = harness.Layer.LastRouteResult?.FormResult ?? FormInputResult.NotHandled;
        harness.Host.Composition.Render();
        return result;
    }

    private static FormInputResult HandleMouse(ScrollableFormDialog form, MouseConsoleInputEvent mouse)
    {
        FormHarness harness = Harnesses.TryGetValue(form, out FormHarness? found)
            ? found
            : throw new InvalidOperationException("Render the form through a test UI layer before dispatching input.");
        harness.Host.Composition.DispatchInput(mouse);
        FormInputResult result = harness.Layer.LastRouteResult?.FormResult ?? FormInputResult.NotHandled;
        harness.Host.Composition.Render();
        return result;
    }

    private static void RequestFocus(ScrollableFormDialog form, string rowId)
    {
        FormHarness harness = Harnesses.TryGetValue(form, out FormHarness? found)
            ? found
            : throw new InvalidOperationException("Render the form through a test UI layer before requesting focus.");
        harness.Layer.RequestFocus(form.GetFocusTarget(rowId));
        harness.Host.Composition.Render();
    }

    private static void RegisterHarness(ScrollableFormDialog form, UiTestHost host, TestFormLayer layer)
    {
        Harnesses.Remove(form);
        Harnesses.Add(form, new FormHarness(host, layer));
    }

    private sealed record FormHarness(UiTestHost Host, TestFormLayer Layer);

    private static IFormRow CreateTextRow(
        bool labeled,
        CommandLineState text,
        SingleLineTextHistoryState? history = null,
        int? width = null,
        bool submitOnEnter = false) =>
        labeled
            ? new LabeledTextInputRow("Value:", text, history, labelWidth: 0, inputWidth: width) { Id = "pattern", SubmitOnEnter = submitOnEnter }
            : new TextInputRow(text, history, width: width) { Id = "pattern", SubmitOnEnter = submitOnEnter };

    private sealed class TestFormLayer(
        ScreenRenderer screen,
        ScrollableFormDialog form,
        Func<UiRenderContext, FormRenderContext> createContext) :
        UiLayer<ScrollableFormFrame>,
        IUiSurface
    {
        public FormRouteResult? LastRouteResult { get; private set; }
        public UiInputRouteKind? LastRouteKind { get; private set; }
        public UiTargetId? LastRouteTarget { get; private set; }
        public bool ThrowOnRender { get; set; }

        public void RequestFocus(UiTargetId target) =>
            RequestFocusOnNextCommit(UiFocusRequest.Set(target));

        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

        public IDisposable BeginFrame(UiRenderRequest request) =>
            screen.BeginFrame();

        public void CompleteFrame(UiFrameCompletion completion)
        {
        }

        protected override ScrollableFormFrame RenderFrame(UiRenderContext context)
        {
            if (ThrowOnRender)
                throw new InvalidOperationException("render failed");

            return form.Render(createContext(context), FocusScope);
        }

        protected override UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame) =>
            form.BuildInteractionFrame(frame);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            ScrollableFormFrame frame,
            UiInputRouteContext context)
        {
            LastRouteKind = context.RouteKind;
            LastRouteTarget = context.Target;
            LastRouteResult = form.RouteInput(input, frame, context);
            return LastRouteResult.Value.UiResult;
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(
        int x,
        int y,
        MouseButton button = MouseButton.Left,
        MouseEventKind kind = MouseEventKind.Down) =>
        new(x, y, button, kind, MouseKeyModifiers.None);

    private sealed class NotHandledFocusableRow(string id) : FormRow
    {
        public override string? Id { get; init; } = id;
        public bool RenderedFocused { get; private set; }

        public override void Render(FormRowRenderContext context)
        {
            RenderedFocused = context.Focused;
            context.Screen.Write(context.Bounds.X, context.Bounds.Y, "row", FarDialogStyles.Fill);
        }

        public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
            FormInputResult.NotHandled;
    }
}
