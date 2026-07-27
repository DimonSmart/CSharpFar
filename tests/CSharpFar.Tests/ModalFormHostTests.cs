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
                    : ModalDialogLoopResult<object?>.Continue;
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

    private static readonly ModalFormOptions Options = new("Test", 30, 8);

    private static ModalFormLayout Layout(ModalDialogRenderer.Layout layout) =>
        new(layout.ContentBounds);

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
