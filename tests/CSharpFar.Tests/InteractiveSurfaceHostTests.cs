using System.Diagnostics;
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
            (context, _) => { context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return ++renders; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (_, frame, _) => new InteractiveSurfaceRouteResult<string>($"frame:{frame}"));

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

    [Fact]
    public void Run_CompletionRestoresTemporarySurfaceOnce()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, _) => ModalDialogLoopResult<bool>.Complete(true));

        Assert.Equal(1, rootRenders);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void Run_ContinuedInputCreatesExactlyOneSubsequentCompositionRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return ++renders;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(
                ((KeyConsoleInputEvent)input).Key.Key,
                Invalidate: true));

        int handled = 0;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, key) =>
            {
                handled++;
                return key == ConsoleKey.Enter
                    ? ModalDialogLoopResult<bool>.Complete(true)
                    : ModalDialogLoopResult<bool>.Continue;
            });

        Assert.Equal(2, handled);
        Assert.Equal(2, renders);
    }

    [Fact]
    public void Run_TimedWakeWithoutInvalidationDoesNotRender()
    {
        var driver = new FakeConsoleDriver();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        int wakes = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => { context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return ++renders; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, _) => ModalDialogLoopResult<bool>.Complete(true),
            getNextWakeUtc: () => wakes == 0 ? DateTimeOffset.UtcNow.AddMilliseconds(1) : null,
            handleWake: _ =>
            {
                wakes++;
                driver.EnqueueKey(Key(ConsoleKey.Enter));
                return InteractiveSurfaceWakeResult.NoChange;
            });

        Assert.Equal(1, wakes);
        Assert.Equal(1, renders);
    }

    [Fact]
    public void Run_TimedWakeWithInvalidationRendersOnce()
    {
        var driver = new FakeConsoleDriver();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        int wakes = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => { context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return ++renders; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, _) => ModalDialogLoopResult<bool>.Complete(true),
            getNextWakeUtc: () => wakes == 0 ? DateTimeOffset.UtcNow.AddMilliseconds(1) : null,
            handleWake: _ =>
            {
                wakes++;
                driver.EnqueueKey(Key(ConsoleKey.Enter));
                return InteractiveSurfaceWakeResult.Changed;
            });

        Assert.Equal(1, wakes);
        Assert.Equal(2, renders);
    }

    [Fact]
    public void Run_PendingInputHasPriorityOverDueWake()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int wakes = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => { context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return 1; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, key) => key == ConsoleKey.Enter
                ? ModalDialogLoopResult<bool>.Complete(true)
                : ModalDialogLoopResult<bool>.Continue,
            getNextWakeUtc: () => DateTimeOffset.UtcNow.AddMilliseconds(-1),
            handleWake: _ =>
            {
                wakes++;
                return InteractiveSurfaceWakeResult.NoChange;
            });

        Assert.Equal(0, wakes);
    }

    [Fact]
    public void Run_ExternalCancellationIsNotTimedWake()
    {
        var driver = new FakeConsoleDriver();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int wakes = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => { context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return 1; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        Assert.Throws<OperationCanceledException>(() => new InteractiveSurfaceHost(composition).Run<int, ConsoleKey, bool>(
            layer,
            (_, _) => ModalDialogLoopResult<bool>.Complete(true),
            getNextWakeUtc: () => DateTimeOffset.UtcNow.AddSeconds(10),
            handleWake: _ =>
            {
                wakes++;
                return InteractiveSurfaceWakeResult.NoChange;
            },
            cancellationToken: cts.Token));
        Assert.Equal(0, wakes);
    }

    [Fact]
    public void Run_RejectedPostInputRenderDoesNotPublishFrameOrMutableState()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new TrackingSurfaceLayer();
        driver.ResizeAfterWriteCount = 2;
        driver.ResizeAfterWrite = current => current.SetSize(100, 35);

        new InteractiveSurfaceHost(composition).Run(
            layer,
            (routed, key) =>
            {
                if (key == ConsoleKey.A)
                    return ModalDialogLoopResult<bool>.Continue;

                Assert.Equal(new ConsoleSize(100, 35), routed.Frame.Viewport.Size);
                Assert.Equal(routed.Frame, layer.MutableStateFrame);
                Assert.DoesNotContain(layer.CommittedFrames, frame => frame.Viewport.Size.Equals(new ConsoleSize(80, 25)) && frame.Sequence > 1);
                return ModalDialogLoopResult<bool>.Complete(true);
            });

        Assert.True(layer.RenderedFrames.Count >= 3);
        Assert.Equal(2, layer.CommittedFrames.Count);
        Assert.Equal(new ConsoleSize(100, 35), layer.CommittedFrames[^1].Viewport.Size);
        Assert.Equal(layer.CommittedFrames[^1], layer.MutableStateFrame);
    }

    [Fact]
    public void Run_ResizeEventRecoversViewportAndDoesNotDispatchResizeAsSemanticInput()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new TrackingSurfaceLayer();
        driver.BeforeReadInput = current => current.SetSize(100, 35);

        int handled = 0;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (routed, key) =>
            {
                handled++;
                Assert.Equal(ConsoleKey.Enter, key);
                Assert.Equal(new ConsoleSize(100, 35), routed.Frame.Viewport.Size);
                return ModalDialogLoopResult<bool>.Complete(true);
            });

        Assert.Equal(1, handled);
        Assert.Equal(2, layer.RenderedFrames.Count);
    }

    [Fact]
    public void Run_ViewportChangeWithoutResizeEventRecoversBeforeDispatchAndKeepsSemanticInput()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new TrackingSurfaceLayer();
        driver.BeforeReadInput = current => current.SetSize(100, 35);

        bool handled = false;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (routed, key) =>
            {
                handled = true;
                Assert.Equal(ConsoleKey.Enter, key);
                Assert.Equal(new ConsoleSize(100, 35), routed.Frame.Viewport.Size);
                return ModalDialogLoopResult<bool>.Complete(true);
            });

        Assert.True(handled);
        Assert.Equal(new ConsoleSize(100, 35), layer.CommittedFrames[^1].Viewport.Size);
    }

    [Fact]
    public void InteractiveSurfaceLayer_RejectsSecondDispatchUntilPendingPacketIsConsumed()
    {
        var driver = new FakeConsoleDriver();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new TrackingSurfaceLayer();
        using var surface = composition.OpenSurface(new InteractiveSurface(screen), layer);
        composition.Render();

        composition.DispatchInput(KeyInput(ConsoleKey.A));

        Assert.Throws<InvalidOperationException>(() => composition.DispatchInput(KeyInput(ConsoleKey.B)));
        Assert.True(layer.TryTakeInput(out var first));
        Assert.Equal(ConsoleKey.A, first.Semantic);

        composition.DispatchInput(KeyInput(ConsoleKey.B));

        Assert.True(layer.TryTakeInput(out var second));
        Assert.Equal(ConsoleKey.B, second.Semantic);
    }

    [Fact]
    public void Run_CancellationClosesSurfaceRendersParentAndLeavesCompositionUsable()
    {
        var driver = new FakeConsoleDriver();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        }));
        var layer = new TrackingSurfaceLayer();

        Assert.Throws<OperationCanceledException>(() => new InteractiveSurfaceHost(composition).Run<SurfaceFrame, ConsoleKey, bool>(
            layer,
            (_, _) => ModalDialogLoopResult<bool>.Complete(true),
            cancellationToken: cts.Token));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
        composition.Render();
        Assert.Equal('R', driver.GetCell(0, 0).Character);

        driver.EnqueueKey(Key(ConsoleKey.Enter));
        bool reopened = new InteractiveSurfaceHost(composition).Run(
            new TrackingSurfaceLayer(),
            (_, _) => ModalDialogLoopResult<bool>.Complete(true));
        Assert.True(reopened);
    }

    [Fact]
    public void MouseCapture_ClosesAndCommittedTargetDisappearanceClearCaptureButRejectedFrameDoesNot()
    {
        var driver = new FakeConsoleDriver();
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new CapturingSurfaceLayer();
        using (composition.OpenSurface(new InteractiveSurface(screen), layer))
        {
            composition.Render();
            composition.DispatchInput(Mouse(1, 1, MouseButton.Left, MouseEventKind.Down));
            Assert.True(layer.TryTakeInput(out _));

            composition.DispatchInput(Mouse(9, 9, MouseButton.Left, MouseEventKind.Move));
            Assert.True(layer.TryTakeInput(out var capturedMove));
            Assert.Equal(UiInputRouteKind.CapturedTarget, capturedMove.Routed.RouteKind);

            driver.SetSize(1, 1);
            driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
            layer.PublishTarget = false;
            driver.ResizeAfterWrite = current =>
            {
                layer.PublishTarget = true;
                current.SetSize(100, 35);
            };
            composition.Render();

            composition.DispatchInput(Mouse(9, 9, MouseButton.Left, MouseEventKind.Move));
            Assert.True(layer.TryTakeInput(out var stillCaptured));
            Assert.Equal(UiInputRouteKind.CapturedTarget, stillCaptured.Routed.RouteKind);

            layer.PublishTarget = true;
            composition.Render();
            composition.DispatchInput(Mouse(1, 1, MouseButton.Left, MouseEventKind.Up));
            Assert.True(layer.TryTakeInput(out var release));
            Assert.Equal(UiInputRouteKind.CapturedTarget, release.Routed.RouteKind);

            composition.DispatchInput(Mouse(1, 1, MouseButton.Left, MouseEventKind.Down));
            Assert.True(layer.TryTakeInput(out _));
        }

        using (composition.OpenSurface(new InteractiveSurface(screen), layer))
        {
            layer.PublishTarget = false;
            composition.Render();
            composition.DispatchInput(Mouse(9, 9, MouseButton.Left, MouseEventKind.Move));
            Assert.True(layer.TryTakeInput(out var ordinaryMove));
            Assert.Equal(UiInputRouteKind.Layer, ordinaryMove.Routed.RouteKind);
        }
    }

    [Fact]
    public void Run_DomainHandlerExecutesAfterDispatchAndCanOpenNestedModal()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var modalHost = new ModalDialogHost(composition);
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        int modalInputs = 0;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, key) =>
            {
                if (key == ConsoleKey.A)
                {
                    modalHost.Run(
                        context => context.Canvas.Write(0, 1, "M", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)),
                        input =>
                        {
                            modalInputs++;
                            return ModalDialogLoopResult<bool>.Complete(true);
                        });
                    return ModalDialogLoopResult<bool>.Continue;
                }

                return ModalDialogLoopResult<bool>.Complete(true);
            });

        Assert.Equal(1, modalInputs);
    }

    [Fact]
    public void Run_ExceptionClosesSurfaceAndRestoresParent()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        Assert.Throws<ApplicationException>(() => new InteractiveSurfaceHost(composition).Run<int, ConsoleKey, bool>(
            layer,
            (_, _) => throw new ApplicationException("boom")));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
        composition.Render();
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void CompositionPump_RendersOverlayInvalidationWhenSurfacePacketIsNotCreated()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.B));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));
        using var surface = composition.OpenSurface(new InteractiveSurface(composition.Screen), layer);
        int overlayRenders = 0;
        using var overlay = composition.PushOverlay(new InvalidatingOnceOverlay(() => overlayRenders++));
        composition.Render();

        var packet = new CompositionInputPump<InteractiveSurfaceInput<int, ConsoleKey>>(
            composition,
            layer.TryTakeInput)
            .Read();

        Assert.Equal(ConsoleKey.B, packet.Semantic);
        Assert.Equal(2, overlayRenders);
    }

    [Fact]
    public void InteractiveSurfaceLayer_CannotBeRegisteredTwiceThroughDifferentLifecycles()
    {
        var composition = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => { }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => 1,
            _ => UiInteractionFrame.Empty,
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        using var first = composition.OpenSurface(new InteractiveSurface(composition.Screen), layer);

        Assert.Throws<InvalidOperationException>(() => composition.OpenSurface(new InteractiveSurface(composition.Screen), layer));
        Assert.Throws<InvalidOperationException>(() => composition.RegisterOverlay(layer));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static KeyConsoleInputEvent KeyInput(ConsoleKey key) => new(Key(key));

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseButton button, MouseEventKind kind) =>
        new(x, y, button, kind, MouseKeyModifiers.None);

    private sealed class InvalidatingOnceOverlay(Action onRender) : IUiLayer
    {
        private bool _handled;

        public UiLayerInputPolicy InputPolicy => _handled ? UiLayerInputPolicy.None : UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) => onRender();

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            _handled = true;
            return UiInputResult.HandledAndInvalidate;
        }
    }
}

public sealed record SurfaceFrame(int Sequence, ConsoleViewport Viewport);

file sealed class TrackingSurfaceLayer : InteractiveSurfaceLayer<SurfaceFrame, ConsoleKey>
{
    private static readonly UiTargetId Keyboard = new("surface.keyboard");
    private int _sequence;

    public TrackingSurfaceLayer()
        : base(
            (_, _) => throw new UnreachableException(),
            _ => throw new UnreachableException(),
            (_, _, _) => throw new UnreachableException())
    {
    }

    public List<SurfaceFrame> RenderedFrames { get; } = [];
    public List<SurfaceFrame> CommittedFrames { get; } = [];
    public SurfaceFrame? MutableStateFrame { get; private set; }

    protected override SurfaceFrame RenderFrameCore(UiRenderContext context)
    {
        context.Canvas.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        var frame = new SurfaceFrame(++_sequence, context.Viewport);
        RenderedFrames.Add(frame);
        return frame;
    }

    protected override UiInteractionFrame BuildInteractionFrameCore(SurfaceFrame frame) =>
        new([], keyboardTarget: Keyboard);

    protected override void OnFrameCommitted(SurfaceFrame frame)
    {
        CommittedFrames.Add(frame);
        MutableStateFrame = frame;
    }

    protected override InteractiveSurfaceRouteResult<ConsoleKey> RouteSemanticInput(
        ConsoleInputEvent input,
        SurfaceFrame frame,
        UiInputRouteContext context) =>
        new(((KeyConsoleInputEvent)input).Key.Key);
}

file sealed class CapturingSurfaceLayer : InteractiveSurfaceLayer<SurfaceFrame, string>
{
    private static readonly UiTargetId Target = new("surface.capture");
    private int _sequence;

    public CapturingSurfaceLayer()
        : base(
            (_, _) => throw new UnreachableException(),
            _ => throw new UnreachableException(),
            (_, _, _) => throw new UnreachableException())
    {
    }

    public bool PublishTarget { get; set; } = true;

    protected override SurfaceFrame RenderFrameCore(UiRenderContext context)
    {
        context.Canvas.Write(0, 0, "C", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        return new SurfaceFrame(++_sequence, context.Viewport);
    }

    protected override UiInteractionFrame BuildInteractionFrameCore(SurfaceFrame frame) =>
        new(PublishTarget ? [new UiHitRegion(Target, new Rect(0, 0, 3, 3))] : []);

    protected override InteractiveSurfaceRouteResult<string> RouteSemanticInput(
        ConsoleInputEvent input,
        SurfaceFrame frame,
        UiInputRouteContext context)
    {
        if (input is MouseConsoleInputEvent { Kind: MouseEventKind.Down, Button: MouseButton.Left })
            return new("down", MouseCaptureRequest: UiMouseCaptureRequest.Capture(Target, MouseButton.Left));

        if (input is MouseConsoleInputEvent { Kind: MouseEventKind.Up, Button: MouseButton.Left })
            return new("up", MouseCaptureRequest: UiMouseCaptureRequest.Release);

        return new("mouse");
    }
}
