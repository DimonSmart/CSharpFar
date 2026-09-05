using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ModalFormHostTests
{
    [Theory]
    [InlineData(ConsoleKey.F10, true)]
    [InlineData(ConsoleKey.Escape, false)]
    [InlineData(ConsoleKey.Spacebar, true)]
    public void Run_NewFieldWithSameHistoryStartsClosedAfterEveryCompletionPath(
        ConsoleKey completionKey,
        bool acceptValue)
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(completionKey));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var id = new TextHistoryId("ModalFormHostTests.Reopen");
        provider.Get(id).Add("saved");
        var factory = new FormFieldFactory(provider);
        TextField first = factory.Text("value", "confirmed", id);
        SingleLineTextHistoryState firstPopup = Assert.IsType<SingleLineTextHistoryState>(first.Input.History);
        Assert.True(firstPopup.OpenAll(availableContentRows: 5));

        host.Run(
            new ScrollableFormDialog([FormControls.Text(first)]),
            Options,
            Layout,
            (_, input) =>
            {
                if (acceptValue)
                    first.AcceptHistory();
                return ModalDialogLoopResult<object?>.Complete(null);
            });

        TextField reopened = factory.Text("value", historyId: id);
        SingleLineTextHistoryState reopenedPopup = Assert.IsType<SingleLineTextHistoryState>(reopened.Input.History);
        Assert.False(reopenedPopup.IsDropdownOpen);
        Assert.Empty(reopenedPopup.Matches);
        Assert.Equal(0, reopenedPopup.SelectedIndex);
        Assert.Equal(0, reopenedPopup.FirstVisibleIndex);
        Assert.Contains("saved", reopenedPopup.History.Items);
        Assert.Equal(acceptValue, reopenedPopup.History.Items.Contains("confirmed"));
    }

    [Fact]
    public void Run_RoutesFormCancelToHandler()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        var form = new ScrollableFormDialog([new LabelRow("Value", DialogStyles.Fill)]);
        FormInputResult handled = FormInputResult.NotHandled;
        ScrollableFormFrame? frame = null;

        bool result = host.Run(
            form,
            Options,
            Layout,
            (routed, input) =>
            {
                frame = routed.Frame;
                handled = input;
                return ModalDialogLoopResult<bool>.Complete(input.Kind == FormInputResultKind.Cancel);
            });

        Assert.True(result);
        Assert.Equal(FormInputResultKind.Cancel, handled.Kind);
        Assert.NotNull(frame);
    }

    [Fact]
    public void Run_ReservesOneColumnInsideEachFrameEdgeForFormContent()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        ScrollableFormFrame? frame = null;
        Rect expectedContent = default;

        host.Run(
            new ScrollableFormDialog([new LabelRow("Value", DialogStyles.Fill)]),
            Options,
            layout =>
            {
                expectedContent = layout.ContentBounds;
                return new ModalFormLayout(layout.ContentBounds);
            },
            (routed, input) =>
            {
                frame = routed.Frame;
                return ModalDialogLoopResult<object?>.Complete(null);
            });

        Assert.NotNull(frame);
        Assert.Equal(expectedContent.X + 1, frame.BodyBounds.X);
        Assert.Equal(expectedContent.Width - 2, frame.BodyBounds.Width);
    }

    [Fact]
    public void Run_PreparesRowsAndUsesCommittedFrameAfterContinuingInput()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        var checkBox = new CheckBoxRow(new CheckBoxLine("Enabled")) { Id = "enabled" };
        var form = new ScrollableFormDialog();
        int preparations = 0;
        var handledFrames = new List<ScrollableFormFrame>();

        host.Run(
            form,
            Options,
            Layout,
            (routed, input) =>
            {
                handledFrames.Add(routed.Frame);
                return input.Kind == FormInputResultKind.Cancel
                    ? ModalDialogLoopResult<object?>.Complete(null)
                    : ModalDialogLoopResult<object?>.ContinueNoChange;
            },
            prepareRender: () =>
            {
                preparations++;
                form.SetRows([checkBox]);
            });

        Assert.Equal(2, preparations);
        Assert.Equal(2, handledFrames.Count);
        Assert.True(checkBox.Value);
        Assert.NotEqual(default, handledFrames[0]);
        Assert.NotEqual(default, handledFrames[1]);
    }

    [Fact]
    public void Run_UsesCustomRenderOptions()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        var outer = DialogStyles.OuterOptions with
        {
            DrawBorder = false,
            DrawShadow = false,
            BackgroundStyle = new CellStyle(ConsoleColor.Red, ConsoleColor.DarkRed),
        };
        var frame = DialogStyles.FrameOptions with { DrawBorder = false, DrawShadow = false };

        host.Run(
            new ScrollableFormDialog([new LabelRow("Value", DialogStyles.Fill)]),
            Options with { OuterRenderOptions = outer, FrameRenderOptions = frame },
            Layout,
            (_, input) => ModalDialogLoopResult<object?>.Complete(null));

        Assert.Contains(driver.WriteRecords, write => write.Background == ConsoleColor.DarkRed);
    }

    [Fact]
    public void Run_CreatesAndDisposesRenderScopeForEachRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        int created = 0;
        int disposed = 0;

        host.Run(
            new ScrollableFormDialog([new CheckBoxRow(new CheckBoxLine("Enabled"))]),
            Options,
            Layout,
            (_, input) => input.Kind == FormInputResultKind.Cancel
                ? ModalDialogLoopResult<object?>.Complete(null)
                : ModalDialogLoopResult<object?>.ContinueNoChange,
            beginRenderScope: () => new CallbackDisposable(() => disposed++, () => created++));

        Assert.Equal(2, created);
        Assert.Equal(created, disposed);
    }

    [Fact]
    public void Run_NaturalSizingIncludesTitleBodyFooterAndComplexRows()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        TextField field = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text(new TextFieldOptions("界", Width: 26));
        var checkBoxes = FormControls.CheckBoxColumns([[FormControls.CheckBox("CheckBox column界 very long")], [FormControls.CheckBox("X")]]);
        var triStateCheckBoxes = new TriStateCheckBoxColumnsRow(
            "Flags",
            [new TriStateCheckBoxLine("Tri-state column界 very long"), new TriStateCheckBoxLine("X")],
            labelWidth: ConsoleTextMetrics.GetCellWidth("Flags"));
        var matrix = FormControls.TriStateMatrix(
            [new("read", "Matrix column界 very long"), new("write", "X")],
            [new("owner", "Very long owner", [CheckState.Checked, CheckState.Unchecked])]);
        var form = new ScrollableFormDialog();
        form.SetRows([FormControls.Label("表題"), FormControls.Text("Name:", field), checkBoxes, triStateCheckBoxes, matrix], [FormControls.OkCancel()]);
        ScrollableFormFrame? frame = null;

        new ModalFormHost(ModalTestHost.Create(driver)).Run(
            form,
            new ModalFormOptions("A wide title界"),
            layout => ModalFormLayout.WithFooter(layout.ContentBounds, 1),
            (routed, input) =>
            {
                frame = routed.Frame;
                return ModalDialogLoopResult<object?>.Complete(null);
            });

        Assert.NotNull(frame);
        Assert.True(frame.BodyBounds.Width >= Math.Max(Math.Max(Math.Max(checkBoxes.DesiredWidth, triStateCheckBoxes.DesiredWidth), matrix.DesiredWidth), 5 + 1 + 26));
        Assert.Contains("CheckBox column界 very long", driver.GetRow(frame.BodyBounds.Y + 2), StringComparison.Ordinal);
        Assert.Contains("Tri-state column界 very long", driver.GetRow(frame.BodyBounds.Y + 3), StringComparison.Ordinal);
        Assert.Contains("Matrix column界 very long", driver.GetRow(frame.BodyBounds.Y + 4), StringComparison.Ordinal);
        Assert.NotNull(frame.FooterBounds);
        Assert.Equal(form.NaturalContentHeight, 2 + checkBoxes.Height + triStateCheckBoxes.Height + matrix.Height + 1);
    }

    [Fact]
    public void Run_TitleOnlyFormUsesTitleForNaturalWidth()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        ScrollableFormFrame? frame = null;

        new ModalFormHost(ModalTestHost.Create(driver)).Run(
            new ScrollableFormDialog(),
            new ModalFormOptions("Title界"),
            Layout,
            (routed, input) =>
            {
                frame = routed.Frame;
                return ModalDialogLoopResult<object?>.Complete(null);
            });

        Assert.NotNull(frame);
        Assert.True(frame.BodyBounds.Width >= ConsoleTextMetrics.GetCellWidth("Title界") + 2);
    }

    [Fact]
    public void Run_AppliesExplicitAndMinimumSizesWithinViewportAndKeepsScrolling()
    {
        var driver = new FakeConsoleDriver(30, 8);
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var form = new ScrollableFormDialog(Enumerable.Range(0, 10).Select(index => (FormRow)FormControls.CheckBox($"Option {index}")).ToArray());
        var frames = new List<ScrollableFormFrame>();

        new ModalFormHost(ModalTestHost.Create(driver)).Run(
            form,
            new ModalFormOptions("Size", PreferredWidth: 40, PreferredHeight: 12, MinWidth: 24, MinHeight: 8),
            Layout,
            (routed, input) =>
            {
                frames.Add(routed.Frame);
                return input.Kind == FormInputResultKind.Cancel
                    ? ModalDialogLoopResult<object?>.Complete(null)
                    : ModalDialogLoopResult<object?>.ContinueChanged;
            });

        Assert.All(frames, frame =>
        {
            Assert.True(frame.BodyBounds.Width <= 24);
            Assert.True(frame.BodyBounds.Height <= 4);
        });
        Assert.Contains(frames[0].Targets, target => target is FormBodyScrollbarTargetFrame);
        Assert.True(frames[^1].EffectiveScrollTop > 0);
    }

    [Fact]
    public void Run_TextTypingBeyondPreferredWidthDoesNotResizeDialog()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        TextField field = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text(new TextFieldOptions("initial", Width: null));
        var frames = new List<ScrollableFormFrame>();

        new ModalFormHost(ModalTestHost.Create(driver)).Run(
            new ScrollableFormDialog([FormControls.Text(field)]),
            new ModalFormOptions("Text"),
            Layout,
            (routed, input) =>
            {
                frames.Add(routed.Frame);
                return input.Kind == FormInputResultKind.Cancel
                    ? ModalDialogLoopResult<object?>.Complete(null)
                    : ModalDialogLoopResult<object?>.ContinueChanged;
            });

        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].BodyBounds.Width, frames[1].BodyBounds.Width);
    }

    [Fact]
    public void Run_CompactChoiceChangeDoesNotResizeDialog()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        CompactChoiceFormRow<string> choice = FormControls.CompactChoice("Mode", ["short", "a much longer value界"], static value => value, "short");
        var frames = new List<ScrollableFormFrame>();

        new ModalFormHost(ModalTestHost.Create(driver)).Run(
            new ScrollableFormDialog([choice]),
            new ModalFormOptions("Choice"),
            Layout,
            (routed, input) =>
            {
                frames.Add(routed.Frame);
                return input.Kind == FormInputResultKind.Cancel
                    ? ModalDialogLoopResult<object?>.Complete(null)
                    : ModalDialogLoopResult<object?>.ContinueChanged;
            });

        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].BodyBounds.Width, frames[1].BodyBounds.Width);
    }

    private static readonly ModalFormOptions Options = new("Test", 30, 8);

    private static ModalFormLayout Layout(ModalDialogRenderer.Layout layout) =>
        new(layout.ContentBounds);

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _dispose;

        public CallbackDisposable(Action dispose, Action created)
        {
            _dispose = dispose;
            created();
        }

        public void Dispose() => _dispose();
    }
}
