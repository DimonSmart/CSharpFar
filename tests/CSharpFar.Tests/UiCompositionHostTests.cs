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

    [Fact]
    public void SurfaceReadInput_ConsumesResizeAndReturnsFollowingKey()
    {
        var driver = new FakeConsoleDriver();
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        using var surface = host.OpenSurface(_ => { });
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var input = surface.ReadInput();

        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void Render_RejectsLayerMutationFromRenderCallback()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ =>
            Assert.Throws<InvalidOperationException>(() => host.OpenSurface(_ => { }))));

        host.Render();
    }

    [Fact]
    public void DisposeSurfaceDuringRender_ThrowsWithoutRemovingSurface()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        UiCompositionHost.UiSurfaceSession? surface = null;
        surface = host.OpenSurface(_ => Assert.Throws<InvalidOperationException>(() => surface!.Dispose()));

        host.Render();
        var nested = host.OpenSurface(_ => { });
        nested.Dispose();
        surface.Dispose();
    }

    [Fact]
    public void DisposeOverlayDuringRender_ThrowsWithoutRemovingOverlay()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var modals = new ModalDialogHost(host);
        ModalDialogSession? overlay = null;
        overlay = modals.Open(_ => Assert.Throws<InvalidOperationException>(() => overlay!.Dispose()));

        overlay.Render();
        overlay.Dispose();
        host.Render();
    }

    [Fact]
    public void OutOfOrderSurfaceDispose_DoesNotInvalidateScope()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var first = host.OpenSurface(_ => { });
        var second = host.OpenSurface(_ => { });

        Assert.Throws<InvalidOperationException>(() => first.Dispose());
        second.Dispose();
        first.Dispose();
    }

    [Fact]
    public void OutOfOrderOverlayDispose_DoesNotInvalidateScope()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var modals = new ModalDialogHost(host);
        var first = modals.Open(_ => { });
        var second = modals.Open(_ => { });

        Assert.Throws<InvalidOperationException>(() => first.Dispose());
        second.Dispose();
        first.Dispose();
    }

    [Fact]
    public void RepeatedSuccessfulDispose_IsSafe()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var surface = host.OpenSurface(_ => { });

        surface.Dispose();
        surface.Dispose();
    }
}
