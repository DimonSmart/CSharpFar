using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ModalDialogRunnerTests
{
    [Fact]
    public void Run_PerformsInitialRenderBeforeReadingInput()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var events = new List<string>();

        modals.Run(
            context =>
            {
                events.Add("render");
                context.Screen.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) =>
            {
                events.Add("input");
                return ModalDialogLoopResult<int>.Complete(0);
            });

        Assert.Equal(["render", "input"], events);
    }

    [Fact]
    public void Run_ContinuePerformsOneAdditionalRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        int renders = 0;

        modals.Run(
            context =>
            {
                renders++;
                context.Screen.Write(0, 0, "M", Style);
                return renders;
            },
            (input, _) => input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }
                ? ModalDialogLoopResult<int>.Complete(0)
                : ModalDialogLoopResult<int>.Continue);

        Assert.Equal(2, renders);
    }

    [Fact]
    public void Run_CompleteDoesNotPerformAdditionalRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        int renders = 0;

        modals.Run(
            context =>
            {
                renders++;
                context.Screen.Write(0, 0, "M", Style);
                return renders;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0));

        Assert.Equal(1, renders);
    }

    [Fact]
    public void Run_HandlerReceivesFrameFromSameReadInputCycle()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.SetSize(100, 35);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        ConsoleSize handled = default;

        modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return context.Size;
            },
            (_, frame) =>
            {
                handled = frame;
                return ModalDialogLoopResult<int>.Complete(0);
            });

        Assert.Equal(new ConsoleSize(100, 35), handled);
    }

    [Fact]
    public void Run_ResizeRecoveryPassesOnlyNewCommittedFrame()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var modals = CreateHost(driver, out _);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.BeforeReadInput = d => d.SetSize(100, 35);
        var applied = new List<ConsoleSize>();
        ConsoleSize handled = default;

        modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return context.Size;
            },
            (_, frame) =>
            {
                handled = frame;
                return ModalDialogLoopResult<int>.Complete(0);
            },
            applyCommittedFrame: applied.Add);

        Assert.Equal(new ConsoleSize(100, 35), handled);
        Assert.Equal(new ConsoleSize(100, 35), applied[^1]);
    }

    [Fact]
    public void Run_RejectedFrameNeverReachesHandler()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var applied = new List<ConsoleSize>();
        ConsoleSize handled = default;

        modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return context.Size;
            },
            (_, frame) =>
            {
                handled = frame;
                return ModalDialogLoopResult<int>.Complete(0);
            },
            applyCommittedFrame: applied.Add);

        Assert.DoesNotContain(new ConsoleSize(80, 25), applied);
        Assert.Equal(new ConsoleSize(100, 35), handled);
    }

    [Fact]
    public void Run_PrepareRenderRunsOutsideCompositionRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out var composition);

        modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0),
            prepareRender: () =>
            {
                using var surface = composition.OpenSurface(_ => { });
            });
    }

    [Fact]
    public void Run_ApplyCommittedFrameRunsOnlyForCommittedFrames()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var applied = new List<ConsoleSize>();

        modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return context.Size;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0),
            applyCommittedFrame: applied.Add);

        Assert.Equal([new ConsoleSize(100, 35), new ConsoleSize(100, 35)], applied);
    }

    [Fact]
    public void Run_HandlerExceptionDisposesOverlay()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => rootRenders++));

        Assert.Throws<InvalidOperationException>(() => modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => throw new InvalidOperationException("handler")));

        Assert.True(rootRenders >= 1);
    }

    [Fact]
    public void Run_RenderExceptionDisposesOverlay()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => rootRenders++));

        Assert.Throws<InvalidOperationException>(() => modals.Run<int, int>(
            _ => throw new InvalidOperationException("render"),
            (_, _) => ModalDialogLoopResult<int>.Complete(0)));

        Assert.True(rootRenders >= 1);
    }

    [Fact]
    public void Run_CancellationDisposesOverlay()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => rootRenders++));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => modals.Run(
            context =>
            {
                context.Screen.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0),
            cancellationToken: cts.Token));

        Assert.True(rootRenders >= 1);
    }

    [Fact]
    public void Run_NestedModalRestoresParentAndContinues()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.N));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var modals = CreateHost(driver, out _);
        int parentRenders = 0;

        modals.Run(
            context =>
            {
                parentRenders++;
                context.Screen.Write(0, 0, "P", Style);
                return 1;
            },
            (input, _) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.N })
                {
                    new MessageDialog(modals).Show("Nested", "Confirm");
                    return ModalDialogLoopResult<int>.Continue;
                }

                return ModalDialogLoopResult<int>.Complete(0);
            });

        Assert.True(parentRenders >= 2);
    }

    [Fact]
    public void SessionReadInput_RoutesToModalLayerAndBlocksLowerSurface()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var lower = new LowerInputSurface(screen);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(lower);
        var modals = new ModalDialogHost(composition);
        using var session = modals.Open(context =>
        {
            context.Screen.Write(0, 0, "M", Style);
            return context.Viewport;
        });
        session.Render();

        var input = session.ReadInput(out var frame);

        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(driver.GetViewport(), frame);
        Assert.Equal(0, lower.RouteCount);
    }

    [Fact]
    public void ModalDialogLayer_RoutedInputIsHandled()
    {
        var driver = new FakeConsoleDriver();
        var screen = new ScreenRenderer(driver);
        var lower = new LowerInputSurface(screen);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(lower);
        var modals = new ModalDialogHost(composition);
        using var session = modals.Open(context =>
        {
            context.Screen.Write(0, 0, "M", Style);
            return context.Viewport;
        });
        session.Render();

        UiInputResult result = composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));

        Assert.True(result.Handled);
        Assert.Equal(0, lower.RouteCount);
        Assert.True(session.TryReadInput(out var input, out _));
        Assert.IsType<KeyConsoleInputEvent>(input);
    }

    [Fact]
    public void GenericSession_ReturnsAlreadyDispatchedFrame()
    {
        var driver = new FakeConsoleDriver();
        var modals = CreateHost(driver, out var composition);
        using var session = modals.Open(context =>
        {
            context.Screen.Write(0, 0, "M", Style);
            return context.Viewport;
        });
        var rendered = session.Render();

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));

        var input = session.ReadInput(out var frame);
        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(rendered, frame);
    }

    [Fact]
    public void RoutedSession_ReturnsPendingPacketWithLayerMetadata()
    {
        var driver = new FakeConsoleDriver
        {
            BeforeReadInput = _ => throw new InvalidOperationException("driver should not be read"),
        };
        var modals = CreateHost(driver, out var composition);
        using var session = modals.Open(context => context.Viewport);
        var frame = session.Render();
        var input = new KeyConsoleInputEvent(Key(ConsoleKey.Enter));
        composition.DispatchInput(input);

        Assert.True(session.TryReadRoutedInput(out var routed));
        Assert.Same(input, routed.Input);
        Assert.Equal(frame, routed.Frame);
        Assert.Null(routed.Target);
        Assert.Equal(UiInputRouteKind.Layer, routed.RouteKind);
        Assert.False(session.TryReadRoutedInput(out _));
    }

    [Fact]
    public void ReadRoutedInput_ReturnsInputFrameAndLayerMetadata()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        using var session = modals.Open(context => context.Viewport);
        var frame = session.Render();

        var routed = session.ReadRoutedInput();

        Assert.IsType<KeyConsoleInputEvent>(routed.Input);
        Assert.Equal(frame, routed.Frame);
        Assert.Null(routed.Target);
        Assert.Equal(UiInputRouteKind.Layer, routed.RouteKind);
    }

    [Fact]
    public void RoutedPacket_KeepsDispatchSnapshotAfterAdditionalRender()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        int render = 0;
        using var session = modals.Open(_ => ++render);
        int initial = session.Render();
        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));
        int later = session.Render();

        var routed = session.ReadRoutedInput();

        Assert.Equal(1, initial);
        Assert.Equal(2, later);
        Assert.Equal(initial, routed.Frame);
        Assert.Null(routed.Target);
        Assert.Equal(UiInputRouteKind.Layer, routed.RouteKind);
    }

    [Fact]
    public void RunRouted_ContinuesWithOneRenderAndPassesRouteMetadata()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        int renders = 0;
        var kinds = new List<UiInputRouteKind>();

        modals.RunRouted(
            context => { renders++; return context.Viewport; },
            routed =>
            {
                kinds.Add(routed.RouteKind);
                return routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }
                    ? ModalDialogLoopResult<int>.Complete(0)
                    : ModalDialogLoopResult<int>.Continue;
            });

        Assert.Equal(2, renders);
        Assert.Equal([UiInputRouteKind.Layer, UiInputRouteKind.Layer], kinds);
    }

    [Fact]
    public void RunRouted_CompleteDoesNotRenderAgain()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        int renders = 0;

        modals.RunRouted(
            _ => ++renders,
            _ => ModalDialogLoopResult<int>.Complete(0));

        Assert.Equal(1, renders);
    }

    [Fact]
    public void RunRouted_HandlerExceptionDisposesOverlayAndRestoresSurface()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => rootRenders++));

        Assert.Throws<InvalidOperationException>(() => modals.RunRouted<int, int>(
            _ => 1,
            _ => throw new InvalidOperationException("handler")));

        Assert.True(rootRenders >= 1);
    }

    [Fact]
    public void RunRouted_CancellationDisposesOverlayAndRestoresSurface()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => rootRenders++));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => modals.RunRouted<int, int>(
            _ => 1,
            _ => ModalDialogLoopResult<int>.Complete(0),
            cancellationToken: cancellation.Token));

        Assert.True(rootRenders >= 1);
    }

    [Fact]
    public void Session_ReturnsAlreadyDispatchedInputWithoutReadingConsole()
    {
        var driver = new FakeConsoleDriver
        {
            BeforeReadInput = _ => throw new InvalidOperationException("driver should not be read"),
        };
        var modals = CreateHost(driver, out var composition);
        using var session = modals.Open(context => context.Screen.Write(0, 0, "M", Style));
        session.Render();

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));

        var input = session.ReadInput();
        Assert.IsType<KeyConsoleInputEvent>(input);
    }

    [Fact]
    public void SecondDispatchBeforeConsumeThrows()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        using var session = modals.Open(context => context.Screen.Write(0, 0, "M", Style));
        session.Render();

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.A)));

        Assert.Throws<InvalidOperationException>(() =>
            composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.B))));
    }

    [Fact]
    public void ConsumedPacketAllowsNextDispatch()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        using var session = modals.Open(context => context.Screen.Write(0, 0, "M", Style));
        session.Render();

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.A)));
        Assert.True(session.TryReadInput(out _));

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.B)));
        Assert.True(session.TryReadInput(out var input));
        Assert.IsType<KeyConsoleInputEvent>(input);
    }

    [Fact]
    public void InputFrameComesFromOriginalDispatchEvenAfterLaterRender()
    {
        var modals = CreateHost(new FakeConsoleDriver(), out var composition);
        int render = 0;
        using var session = modals.Open(context =>
        {
            render++;
            context.Screen.Write(0, 0, "M", Style);
            return render;
        });
        int frameA = session.Render();

        composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)));
        int frameB = session.Render();
        var input = session.ReadInput(out var inputFrame);

        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(1, frameA);
        Assert.Equal(2, frameB);
        Assert.Equal(frameA, inputFrame);
    }

    private static ModalDialogHost CreateHost(FakeConsoleDriver driver, out UiCompositionHost composition)
    {
        var screen = new ScreenRenderer(driver);
        composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        return new ModalDialogHost(composition);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static CellStyle Style => new(ConsoleColor.Gray, ConsoleColor.Black);

    private sealed class LowerInputSurface(ScreenRenderer screen) : IUiSurface, IUiLayer
    {
        public int RouteCount { get; private set; }
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) { }
        public void CompleteFrame(UiFrameCompletion completion) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            RouteCount++;
            return UiInputResult.NotHandled;
        }
    }
}
