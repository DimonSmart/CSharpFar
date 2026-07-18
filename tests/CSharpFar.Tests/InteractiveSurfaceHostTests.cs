using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class InteractiveSurfaceHostTests
{
    [Fact]
    public void Run_PublishesInitialFrameAndDoesNotRenderAfterCompletion()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        var layer = new InteractiveSurfaceLayer<int, string>(
            (context, _) => { context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return ++renders; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (_, frame, _) => ($"frame:{frame}", UiInputResult.HandledResult));

        string result = new InteractiveSurfaceHost(composition).Run(
            layer,
            (routed, semantic) =>
            {
                Assert.Equal(1, routed.Frame);
                return ModalDialogLoopResult<string>.Complete(semantic);
            });

        Assert.Equal("frame:1", result);
        Assert.Equal(1, renders);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
