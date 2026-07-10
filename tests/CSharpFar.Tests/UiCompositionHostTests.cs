using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiCompositionHostTests
{
    [Fact]
    public void Render_RendersRootThenOverlaysInInsertionOrder()
    {
        var driver = new FakeConsoleDriver();
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        var rendered = new List<string>();
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => rendered.Add("root")));

        using var first = host.PushOverlay(_ => rendered.Add("first"));
        using var second = host.PushOverlay(_ => rendered.Add("second"));
        host.Render();

        Assert.Equal(["root", "first", "second"], rendered);
    }

    [Fact]
    public void TemporarySurface_HidesRootAndDisposalRestoresIt()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var rendered = new List<string>();
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => rendered.Add("root")));

        using (host.OpenSurface(_ => rendered.Add("editor")))
            host.Render();
        host.Render();

        Assert.Equal(["editor", "root"], rendered);
    }

    [Fact]
    public void ModalReadInput_ConsumesResizeAndReturnsFollowingKey()
    {
        var driver = new FakeConsoleDriver();
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var modals = new ModalDialogHost(host);
        using var modal = modals.Open(_ => { });
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var input = modal.ReadInput();

        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void ModalDisposal_RedrawsTheRemainingComposition()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var rendered = new List<string>();
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => rendered.Add("root")));
        var modals = new ModalDialogHost(host);
        var modal = modals.Open(_ => rendered.Add("modal"));
        modal.Render();
        modal.Dispose();

        Assert.Equal(["root", "modal", "root"], rendered);
    }
}
