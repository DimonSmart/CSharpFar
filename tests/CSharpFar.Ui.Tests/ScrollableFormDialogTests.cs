using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    private static readonly ConditionalWeakTable<ScrollableFormDialog, FormHarness> Harnesses = new();

    [Fact]
    public void Mnemonics_FromPublicFormControlsFocusTheirAssociatedControlsAndDoNotRenderAmpersands()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField text = fields.Text();
        ChoiceFormRow<string> choice = FormControls.Choice("&Choice", ["one", "two"], static value => value, "one");
        CompactChoiceFormRow<string> compact = FormControls.CompactChoice("&Quick", ["one", "two"], static value => value, "one");
        MultiLineChoiceFormRow<string> multi = FormControls.MultiLineChoice("&Multi", ["one", "two"], static value => value, "one", itemsPerRow: 1);
        DropdownSelectFormRow<string> dropdown = FormControls.Dropdown("&Pick", ["one", "two"], static value => value, "one");
        var form = new ScrollableFormDialog([
            FormControls.Text("&Host:", text), choice, compact, multi, dropdown,
        ]);

        FakeConsoleDriver driver = Render(form, visibleRows: 5, screenHeight: 8);

        Assert.DoesNotContain('&', string.Concat(Enumerable.Range(0, 5).Select(driver.GetRow)));
        AssertMnemonic(form, 'c', choice);
        AssertMnemonic(form, 'q', compact);
        AssertMnemonic(form, 'm', multi);
        AssertMnemonic(form, 'p', dropdown);
        AssertMnemonic(form, 'h', text);
    }

    [Fact]
    public void Mnemonic_SkipsDisabledDuplicateAndUsesFirstEnabledControlInFormOrder()
    {
        CheckBoxRow disabled = FormControls.CheckBox("&Host", enabled: false, disabledReason: "Unavailable");
        CheckBoxRow firstEnabled = FormControls.CheckBox("&Host");
        CheckBoxRow secondEnabled = FormControls.CheckBox("&Host");
        var form = new ScrollableFormDialog([disabled, firstEnabled, secondEnabled]);
        Render(form, visibleRows: 3);

        AssertMnemonic(form, 'h', firstEnabled);
    }

    [Fact]
    public void Scrollbar_ReservesTheLastBodyColumnForRowsAndHistoryArrow()
    {
        var history = TextFieldHistoryTestProvider.CreateState(["saved"]);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "input" },
            new LabelRow("second", DialogStyles.Fill),
        ]);
        var driver = new FakeConsoleDriver(20, 4);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(context, new Rect(0, 0, 20, 1), DialogStyles.Border));
        UiTestHost.Create(screen, layer).Composition.Render();

        FormTargetFrame row = Assert.Single(layer.CommittedFrame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("input"));
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.BodyScrollbar);

        Assert.Equal(scrollbar.Bounds.X, row.Bounds.Right);
        Assert.Equal(SingleLineTextInput.HistoryDropdownArrow, driver.GetCell(row.Bounds.Right - 1, row.Bounds.Y).Character);
    }

    [Fact]
    public void FocusedRowId_ReturnsCurrentFocusableRowId()
    {
        var form = new ScrollableFormDialog([
            new LabelRow("label", DialogStyles.Fill),
            new TextInputRow(new CommandLineState()) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" },
        ]);
        Render(form, visibleRows: 3);

        Assert.Equal("first", form.FocusedRowId);

        HandleKey(form, Key(ConsoleKey.Tab));

        Assert.Equal("second", form.FocusedRowId);
    }

    [Fact]
    public void AnonymousControlsReceiveIndependentRuntimeTargets()
    {
        CheckBoxRow first = FormControls.CheckBox("First");
        CheckBoxRow second = FormControls.CheckBox("Second");
        var form = new ScrollableFormDialog([first, second]);

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 2);
        FormRowTargetFrame[] targets = frame.Targets.OfType<FormRowTargetFrame>().ToArray();

        Assert.All(targets, target => Assert.Null(target.Row.Id));
        Assert.Equal(2, targets.Length);
        Assert.NotEqual(targets[0].Target, targets[1].Target);
    }

    [Fact]
    public void FocusRequest_MovesFocusToRowByIdOnCommit()
    {
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("first")) { Id = "first" },
            new LabelRow("label", DialogStyles.Fill),
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
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), DialogStyles.Border));
        UiTestHost.Create(screen, layer).Composition.Render();

        FormTargetFrame target = Assert.Single(layer.CommittedFrame.Targets, target => target.Target == FormTargetIds.ForExplicitRow("last"));
        Assert.Equal("last", form.FocusedRowId);
        Assert.Equal(3, form.ScrollTop);
        Assert.NotNull(target.HitBounds);
        Assert.True(driver.CursorVisible);
    }

    [Fact]
    public void SetInitialFocus_AcceptsTypedAnonymousControl()
    {
        CheckBoxRow first = FormControls.CheckBox("First");
        CheckBoxRow target = FormControls.CheckBox("Target");
        var form = new ScrollableFormDialog([first, target]);

        form.SetInitialFocus(target);

        Assert.True(form.IsFocused(target));
        Assert.False(form.IsFocused(first));
    }

    [Fact]
    public void IsFocused_AcceptsTypedControlAfterNavigation()
    {
        CheckBoxRow first = FormControls.CheckBox("First");
        CheckBoxRow second = FormControls.CheckBox("Second");
        var form = new ScrollableFormDialog([first, second]);
        Render(form, visibleRows: 2);

        HandleKey(form, Key(ConsoleKey.Tab));

        Assert.False(form.IsFocused(first));
        Assert.True(form.IsFocused(second));
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
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), DialogStyles.Border));

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
            new LabelRow("label", DialogStyles.Fill),
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
            new LabelRow("label", DialogStyles.Fill) { Id = "label" },
            new TextInputRow(new CommandLineState()) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("second")) { Id = "second" },
            new CheckBoxRow(new CheckBoxLine("third")) { Id = "third" },
        ]);

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 2);
        UiInteractionFragment fragment = form.BuildInteractionFragment(frame);
        UiInteractionFrame interaction = form.BuildInteractionFrame(frame);

        Assert.Contains(frame.Targets, target => target is FormRowTargetFrame { Target: var id, IsFocusable: true } && id == FormTargetIds.ForExplicitRow("first"));
        Assert.Contains(frame.Targets, target => target is FormRowTargetFrame { Target: var id, IsFocusable: false } && id == FormTargetIds.ForExplicitRow("label"));
        Assert.Contains(frame.Targets, target => target is { Kind: FormTargetKind.BodyScrollbar, Target: var id } && id == FormTargetIds.BodyScrollbar);
        Assert.Equal(3, interaction.Focus.Entries.Count);
        Assert.Contains(interaction.HitRegions, region => region.Target == FormTargetIds.BodyScrollbar);
        Assert.Equal(fragment.HitRegions, interaction.HitRegions);
        Assert.Equal(fragment.FocusEntries, interaction.Focus.Entries);
        Assert.Equal(frame.DefaultTarget, interaction.Focus.DefaultTarget);
    }

    [Fact]
    public void InteractionFragment_CanBeCombinedWithTableListFragment()
    {
        var form = new ScrollableFormDialog([new CheckBoxRow(new CheckBoxLine("option"))]);
        ScrollableFormFrame formFrame = RenderFrame(form, visibleRows: 1);
        var table = new TableList<int>(
            [1, 2],
            new TableListDefinition<int>
            {
                Columns = [TableColumn<int>.Text("Value", value => value.ToString(), 8)],
            });
        TableListFrame tableFrame = table.CalculateFrame(new Rect(0, 2, 20, 3));

        UiInteractionFrame interaction = new UiInteractionFrameBuilder()
            .AddFragment(table.BuildInteractionFragment(tableFrame, 1))
            .AddFragment(form.BuildInteractionFragment(formFrame))
            .SetDefaultFocusTarget(formFrame.DefaultTarget)
            .Build();

        Assert.NotEmpty(interaction.HitRegions);
        Assert.Contains(interaction.Focus.Entries, entry => entry.TabOrder == 1);
        Assert.Equal(formFrame.DefaultTarget, interaction.Focus.DefaultTarget);
    }

    [Fact]
    public void RenderFrame_PublishesHistoryDropdownTargetsForFocusedRow()
    {
        var history = TextFieldHistoryTestProvider.CreateState();
        history.History.Add("alpha");
        history.History.Add("beta");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
        ]);
        Assert.True(SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY: 0, screenHeight: 8));

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 1, screenHeight: 8);

        Assert.Contains(frame.Targets, target => target is { Kind: FormTargetKind.HistoryDropdown, Target: var id } && id == FormTargetIds.ForHistoryDropdown(FormTargetIds.ForExplicitRow("pattern")));
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
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            ])
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
        var history = TextFieldHistoryTestProvider.CreateState();
        for (int i = 0; i < count; i++)
            history.History.Add($"item-{i:D2}");
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
                DialogStyles.Border));
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
                DialogStyles.Border));
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
                DialogStyles.Border,
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
                DialogStyles.Border));
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
                DialogStyles.Border,
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

    private static void AssertMnemonic(ScrollableFormDialog form, char keyChar, IFormFocusTarget target)
    {
        HandleKey(form, new ConsoleKeyInfo(keyChar, (ConsoleKey)char.ToUpperInvariant(keyChar), false, true, false));
        Assert.True(form.IsFocused(target));
    }

    private static void RegisterHarness(ScrollableFormDialog form, UiTestHost host, TestFormLayer layer)
    {
        Harnesses.Remove(form);
        Harnesses.Add(form, new FormHarness(host, layer));
    }

    private sealed record FormHarness(UiTestHost Host, TestFormLayer Layer);

    private static FormRow CreateTextRow(
        bool labeled,
        CommandLineState text,
        SingleLineTextHistoryState? history = null,
        int? width = null,
        bool submitOnEnter = false) =>
        labeled
            ? new LabeledTextInputRow("Value:", text, history, inputWidth: width) { Id = "pattern", SubmitOnEnter = submitOnEnter }
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

            return form.Render(createContext(context), FocusState);
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

        internal override void Render(FormRowRenderContext context)
        {
            RenderedFocused = context.Focused;
            context.Canvas.Write(context.Bounds.X, context.Bounds.Y, "row", DialogStyles.Fill);
        }

        internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
            FormInputResult.NotHandled;
    }
}
