using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ModalFormHostTests
{
    [Fact]
    public void Run_RoutesFormCancelToHandler()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var host = new ModalFormHost(ModalTestHost.Create(driver));
        var form = new ScrollableFormDialog([new LabelRow("Value", FarDialogStyles.Fill)]);
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
            new ScrollableFormDialog([new LabelRow("Value", FarDialogStyles.Fill)]),
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
        var outer = FarDialogStyles.OuterOptions with
        {
            DrawBorder = false,
            DrawShadow = false,
            BackgroundStyle = new CellStyle(ConsoleColor.Red, ConsoleColor.DarkRed),
        };
        var frame = FarDialogStyles.FrameOptions with { DrawBorder = false, DrawShadow = false };

        host.Run(
            new ScrollableFormDialog([new LabelRow("Value", FarDialogStyles.Fill)]),
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
