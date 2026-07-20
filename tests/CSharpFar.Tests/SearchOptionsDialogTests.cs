using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SearchOptionsDialogTests
{
    [Fact]
    public void SearchOptionsDialog_PatternRow_HasStableIdAndSubmitOnEnter()
    {
        IReadOnlyList<IFormRow> rows = BuildRows();

        TextInputRow patternRow = Assert.Single(rows.OfType<TextInputRow>());
        Assert.Equal("pattern", patternRow.Id);
        Assert.Equal(FormRowRole.TextInput, patternRow.Role);
        Assert.True(patternRow.SubmitOnEnter);
    }

    [Fact]
    public void SearchOptionsDialog_EnterOnPattern_SubmitsFind()
    {
        var history = new SingleLineTextHistoryState();
        var form = new ScrollableFormDialog(BuildRows(history));

        FormInputResult result = HandleKey(form, history, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("find", result.Command);
    }

    [Fact]
    public void SearchOptionsDialog_EnterOnPatternWithOpenHistory_DoesNotSubmitDirectly()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("saved pattern");
        Assert.True(history.OpenAll(availableContentRows: 1));
        var form = new ScrollableFormDialog(BuildRows(history));

        FormInputResult result = HandleKey(form, history, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Null(result.Command);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void SearchOptionsDialog_EnterOnCheckbox_DoesNotSubmitFind()
    {
        var history = new SingleLineTextHistoryState();
        var form = new ScrollableFormDialog(BuildRows(history));
        form.SetInitialFocus("option");

        FormInputResult result = HandleKey(form, history, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.Null(result.Command);
    }

    [Fact]
    public void SearchOptionsDialog_DoesNotDependOnNumericFocusIndex()
    {
        var history = new SingleLineTextHistoryState();
        var rows = new List<IFormRow>
        {
            new CheckBoxRow(new CheckBoxLine("Before pattern")) { Id = "before-pattern" },
        };
        rows.AddRange(BuildRows(history));
        var form = new ScrollableFormDialog(rows);
        form.SetInitialFocus("pattern");
        Assert.NotEqual(0, form.FocusIndex);

        FormInputResult result = HandleKey(form, history, Key(ConsoleKey.Enter));

        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("find", result.Command);
    }

    [Fact]
    public void Show_EnterConfirmsInitialPattern()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.Equal("abc", result.Pattern);
    }

    [Fact]
    public void Show_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.Null(result);
    }

    [Fact]
    public void Show_MouseClickButtonConfirms()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record => record.Text.Contains("{ Find }", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Find", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        };

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.Equal("abc", result.Pattern);
    }

    [Fact]
    public void Show_MouseClickCheckboxTogglesOption()
    {
        var driver = new FakeConsoleDriver(80, 25);
        int inputIndex = 0;
        driver.BeforeReadInput = currentDriver =>
        {
            if (inputIndex++ == 0)
            {
                var row = currentDriver.WriteRecords.Last(record => record.Text.Contains("Case sensitive", StringComparison.Ordinal));
                int x = row.X + row.Text.IndexOf("Case sensitive", StringComparison.Ordinal);
                currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
                currentDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
                return;
            }
        };

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.True(result.GetOption("case-sensitive"));
    }

    [Fact]
    public void Show_KeyboardNavigationTogglesOptionAndConfirms()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Spacebar, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.True(result.GetOption("case-sensitive"));
    }

    private static SearchOptionsDialogResult? ShowDialog(FakeConsoleDriver driver, string initialPattern)
    {
        var modalDialogs = ModalTestHost.Create(driver);
        return new SearchOptionsDialog(modalDialogs).Show(new SearchOptionsDialogOptions
        {
            InitialPattern = initialPattern,
            HistoryKey = $"SearchOptionsDialogTests:{Guid.NewGuid()}",
            Width = 56,
            Options =
            [
                new SearchOptionLine("case-sensitive", "Case sensitive", false),
                new SearchOptionLine("whole-words", "Whole words", false),
            ],
        });
    }

    private static IReadOnlyList<IFormRow> BuildRows(SingleLineTextHistoryState? history = null)
    {
        history ??= new SingleLineTextHistoryState();
        return SearchOptionsDialog.BuildRows(
            new SearchOptionsDialogOptions(),
            new CommandLineState(),
            history,
            new TextInputRowState(),
            [new CheckBoxRow(new CheckBoxLine("Option")) { Id = "option" }],
            new ButtonRow(
                [new DialogButton("find", "Find", 'F', IsDefault: true)],
                FarDialogStyles.Fill,
                FarDialogStyles.FocusedInput));
    }

    private static FormInputResult HandleKey(
        ScrollableFormDialog form,
        SingleLineTextHistoryState history,
        ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.F10 ||
            key.Key == ConsoleKey.Enter && form.IsFocusedOnSubmitRow && !history.IsDropdownOpen)
        {
            return FormInputResult.Submit("find");
        }

        var driver = new FakeConsoleDriver(80, 25);
        var screen = new ScreenRenderer(driver);
        var layer = new SearchOptionsFormLayer(screen, form);
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(key));
        return layer.LastResult?.FormResult ?? FormInputResult.NotHandled;
    }

    private sealed class SearchOptionsFormLayer(ScreenRenderer screen, ScrollableFormDialog form) :
        UiLayer<ScrollableFormFrame>,
        IUiSurface
    {
        public FormRouteResult? LastResult { get; private set; }

        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void CompleteFrame(UiFrameCompletion completion)
        {
        }

        protected override ScrollableFormFrame RenderFrame(UiRenderContext context) =>
            form.Render(
                new FormRenderContext(context, new Rect(0, 0, 80, 10), FarDialogStyles.Border),
                FocusScope);

        protected override UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame) =>
            form.BuildInteractionFrame(frame);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            ScrollableFormFrame frame,
            UiInputRouteContext context)
        {
            LastResult = form.RouteInput(input, frame, context);
            return LastResult.Value.UiResult;
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
