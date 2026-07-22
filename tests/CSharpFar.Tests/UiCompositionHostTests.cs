using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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
    public void Render_InteractiveSurfaceUsesLayerRenderNotSurfaceRender()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var surface = new SplitSurface(host.Screen);
        host.SetRootSurface(surface);

        host.Render();

        Assert.Equal(1, surface.BeginCount);
        Assert.Equal(1, surface.LayerRenderCount);
        Assert.Equal(0, surface.SurfaceRenderCount);
        Assert.Equal(1, surface.CompleteCount);
    }

    [Fact]
    public void InteractiveLayer_CannotBeRegisteredTwice()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var layer = new TestInteractiveLayer(UiLayerInputPolicy.Bubble);

        using var scope = host.PushOverlay(layer);

        Assert.Throws<InvalidOperationException>(() => host.PushOverlay(layer));
    }

    [Fact]
    public void InteractiveLayer_CannotBeRegisteredTwiceWhenInputPolicyIsNone()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        var layer = new TestInteractiveLayer(UiLayerInputPolicy.None);

        using var scope = host.RegisterOverlay(layer);

        Assert.Throws<InvalidOperationException>(() => host.RegisterOverlay(layer));
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
        overlay = modals.Open((Action<UiRenderContext>)(_ => Assert.Throws<InvalidOperationException>(() => overlay!.Dispose())));

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

    [Fact]
    public void TryReadInput_RedrawsViewportChangeWhenInputQueueIsEmpty()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, context => Fill(context, 'R')));
        var modals = new ModalDialogHost(host);
        using var modal = modals.Open(context => CenterMark(context, 'M'));

        modal.Render();
        driver.SetSize(100, 35);

        bool hasInput = modal.TryReadInput(out var input);

        Assert.False(hasInput);
        Assert.Null(input);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
        Assert.Equal('R', driver.GetCell(99, 34).Character);
        Assert.Equal('M', driver.GetCell(50, 17).Character);
    }

    [Fact]
    public void TryReadInput_ConsumesResizeEventsAndPreservesOrdinaryInput()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        int renders = 0;
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        using var surface = host.OpenSurface(_ => renders++);
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false));

        bool hasInput = surface.TryReadInput(out var input);

        Assert.True(hasInput);
        var keyInput = Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(ConsoleKey.X, keyInput.Key.Key);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
        Assert.Equal(1, renders);
    }

    [Fact]
    public void ReadInput_ViewportChangedBeforeSemanticKey_DoesNotLoseKey()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        int renders = 0;
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => renders++));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        var input = host.ReadInput();

        Assert.Equal(ConsoleKey.X, Assert.IsType<KeyConsoleInputEvent>(input).Key.Key);
        Assert.Equal(2, renders);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void TryReadInput_ViewportChangedBeforeSemanticKey_DoesNotLoseKey()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        Assert.True(host.TryReadInput(out var input));

        Assert.Equal(ConsoleKey.X, Assert.IsType<KeyConsoleInputEvent>(input).Key.Key);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void ReadInput_ViewportChangedBeforeMouse_DoesNotLoseMouse()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueInput(new MouseConsoleInputEvent(2, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var input = host.ReadInput();

        Assert.IsType<MouseConsoleInputEvent>(input);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void TryReadInput_ViewportChangedBeforeModifier_DoesNotLoseModifier()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));

        Assert.True(host.TryReadInput(out var input));

        Assert.IsType<ModifierKeyConsoleInputEvent>(input);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
    }

    [Fact]
    public void PollingResizeRecovery_DoesNotRenderRepeatedlyWhenStable()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        int renders = 0;
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        using var surface = host.OpenSurface(_ => renders++);
        surface.Render();
        driver.SetSize(100, 35);

        Assert.False(surface.TryReadInput(out _));
        Assert.Equal(2, renders);

        Assert.False(surface.TryReadInput(out _));
        Assert.False(surface.TryReadInput(out _));
        Assert.Equal(2, renders);
    }

    [Fact]
    public void HelpViewer_ResizeThenClose_RestoresCurrentRootSurface()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, context => Fill(context, 'V')));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, false, false, false));

        new HelpViewer(host).Show();

        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
        Assert.Equal('V', driver.GetCell(99, 34).Character);
    }

    [Fact]
    public void InterruptedFlush_DiscardsPendingLayoutAndPublishesOnlyNewViewport()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 5,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        var published = new List<ConsoleViewport>();
        var completions = new List<UiFrameCompletion>();
        host.SetRootSurface(new RecordingSurface(screen, context =>
        {
            Fill(context, 'R');
            context.PublishOnStable(context.Viewport, published.Add);
        }, completions));

        host.Render();

        Assert.Equal([new ConsoleViewport(0, 0, 100, 35)], published);
        Assert.Equal(2, completions.Count);
        Assert.False(completions[0].WasCommitted);
        Assert.True(completions[0].WasInterrupted);
        Assert.True(completions[1].WasCommitted);
        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
        Assert.Equal('R', driver.GetCell(99, 34).Character);
        Assert.Equal(1, host.StableRenderVersion);
    }

    [Fact]
    public void StableRenderVersion_IncrementsOnlyForCommittedFrames()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, context => Fill(context, 'R')));

        host.Render();
        Assert.Equal(1, host.StableRenderVersion);

        host.Render();
        Assert.Equal(2, host.StableRenderVersion);
    }

    [Fact]
    public void CommittedState_RejectedAttemptDoesNotBecomeVisible()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        var state = new UiCommittedState<ConsoleViewport>();
        var observedBeforeCommit = new List<bool>();
        var publications = new List<string>();
        host.SetRootSurface(new ScreenRendererSurface(screen, context =>
        {
            observedBeforeCommit.Add(state.HasValue);
            state.Stage(context, context.Viewport);
            context.PublishOnStable(() => publications.Add("committed"));
            Fill(context, 'S');
        }));

        host.Render();

        Assert.Equal([false, false], observedBeforeCommit);
        Assert.Equal(["committed"], publications);
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), state.Value);
    }

    [Fact]
    public void GenericModalRender_ReturnsOnlyCommittedFrameAfterRejectedAttempt()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, FillRoot));
        var modals = new ModalDialogHost(host);
        var attempted = new List<ConsoleViewport>();

        using var session = modals.Open(context =>
        {
            attempted.Add(context.Viewport);
            context.Canvas.Write(0, 0, "M", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
            return context.Viewport;
        });

        var frame = session.Render();

        Assert.Equal(
            [new ConsoleViewport(0, 0, 80, 25), new ConsoleViewport(0, 0, 100, 35)],
            attempted);
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), frame);
    }

    [Fact]
    public void GenericModalReadInput_ReturnsFrameAfterResizeRecovery()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, FillRoot));
        var modals = new ModalDialogHost(host);
        int renderCount = 0;
        using var session = modals.Open(context =>
        {
            renderCount++;
            context.Canvas.Write(0, 0, "M", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
            return context.Viewport;
        });
        session.Render();
        driver.SetSize(100, 35);
        driver.EnqueueInput(new MouseConsoleInputEvent(2, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var input = session.ReadInput(out var frame);

        Assert.IsType<MouseConsoleInputEvent>(input);
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), frame);
        Assert.Equal(2, renderCount);
    }

    private static void Fill(UiRenderContext context, char value)
    {
        var style = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        string row = new(value, context.Size.Width);
        for (int y = 0; y < context.Size.Height; y++)
            context.Canvas.Write(0, y, row, style);
    }

    private static void FillRoot(UiRenderContext context) => Fill(context, 'R');

    private static void CenterMark(UiRenderContext context, char value)
    {
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.Blue);
        context.Canvas.Write(context.Size.Width / 2, context.Size.Height / 2, value.ToString(), style);
    }

    private sealed class RecordingSurface : IUiSurface
    {
        private readonly ScreenRenderer _screen;
        private readonly Action<UiRenderContext> _render;
        private readonly List<UiFrameCompletion> _completions;

        public RecordingSurface(ScreenRenderer screen, Action<UiRenderContext> render, List<UiFrameCompletion> completions) =>
            (_screen, _render, _completions) = (screen, render, completions);

        public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
        public void Render(UiRenderContext context) => _render(context);
        public void CompleteFrame(UiFrameCompletion completion) => _completions.Add(completion);
    }

    private sealed class SplitSurface(ScreenRenderer screen) : IUiSurface, IUiLayer
    {
        public int BeginCount { get; private set; }
        public int SurfaceRenderCount { get; private set; }
        public int LayerRenderCount { get; private set; }
        public int CompleteCount { get; private set; }

        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;

        public IDisposable BeginFrame(UiRenderRequest request)
        {
            BeginCount++;
            return screen.BeginFrame();
        }

        void IUiSurface.Render(UiRenderContext context) => SurfaceRenderCount++;

        void IUiLayer.Render(UiRenderContext context) => LayerRenderCount++;

        public void CompleteFrame(UiFrameCompletion completion) => CompleteCount++;

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            UiInputResult.NotHandled;
    }

    private sealed class TestInteractiveLayer(UiLayerInputPolicy policy) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            UiInputResult.NotHandled;
    }
}
