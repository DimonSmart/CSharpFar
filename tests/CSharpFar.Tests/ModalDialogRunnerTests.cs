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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
                return renders;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0));

        Assert.Equal(1, renders);
    }

    [Fact]
    public void Run_CompletionRestoresUnderlyingSurfaceImmediately()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out var composition);
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
            context.Canvas.Write(0, 0, "R", Style)));

        modals.Run(
            context =>
            {
                context.Canvas.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0));

        Assert.Equal('R', driver.GetCell(0, 0).Character);
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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
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
                context.Canvas.Write(0, 0, "M", Style);
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
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", Style);
        }));

        Assert.Throws<InvalidOperationException>(() => modals.Run(
            context =>
            {
                context.Canvas.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => throw new InvalidOperationException("handler")));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void Run_RenderExceptionDisposesOverlay()
    {
        var driver = new FakeConsoleDriver();
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", Style);
        }));

        Assert.Throws<InvalidOperationException>(() => modals.Run<int, int>(
            _ => throw new InvalidOperationException("render"),
            (_, _) => ModalDialogLoopResult<int>.Complete(0)));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void Run_CancellationDisposesOverlay()
    {
        var driver = new FakeConsoleDriver();
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", Style);
        }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => modals.Run(
            context =>
            {
                context.Canvas.Write(0, 0, "M", Style);
                return 1;
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(0),
            cancellationToken: cts.Token));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
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
                context.Canvas.Write(0, 0, "P", Style);
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
    public void RunRouted_AppliesRoutedFrameImmediatelyBeforeHandler()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var events = new List<string>();
        int frame = 0;

        modals.RunRouted(
            _ =>
            {
                events.Add("render");
                return ++frame;
            },
            routed =>
            {
                events.Add($"handler:{routed.Frame}");
                return routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }
                    ? ModalDialogLoopResult<int>.Complete(0)
                    : ModalDialogLoopResult<int>.Continue;
            },
            applyCommittedFrame: committed => events.Add($"apply:{committed}"));

        Assert.Equal(
            ["render", "apply:1", "apply:1", "handler:1", "render", "apply:2", "apply:2", "handler:2"],
            events);
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
    public void RunInteractive_HandlesDomainBeforeInvalidationRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var events = new List<string>();

        int result = modals.RunInteractive<int, string, int>(
            (context, _) =>
            {
                events.Add("render");
                context.Canvas.Write(0, 0, "M", Style);
                return 1;
            },
            _ => UiInteractionFrame.Empty,
            (input, _, _) =>
            {
                events.Add("route");
                return input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Spacebar }
                    ? ("continue", UiInputResult.HandledAndInvalidate)
                    : ("complete", UiInputResult.HandledAndInvalidate);
            },
            (_, semantic) =>
            {
                events.Add("domain");
                return semantic == "complete"
                    ? ModalDialogLoopResult<int>.Complete(42)
                    : ModalDialogLoopResult<int>.Continue;
            },
            prepareRender: () => events.Add("prepare"));

        Assert.Equal(42, result);
        Assert.Equal(["prepare", "render", "route", "domain", "prepare", "render", "route", "domain"], events);
    }

    [Fact]
    public void RunInteractive_DoesNotRestoreCommittedFrameBetweenRouteAndDomainHandler()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var events = new List<string>();
        int state = 0;

        int result = modals.RunInteractive<int, bool, int>(
            (_, _) =>
            {
                events.Add($"render:{state}");
                return state;
            },
            _ => UiInteractionFrame.Empty,
            (input, _, _) =>
            {
                events.Add("route");
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Spacebar })
                {
                    state = 1;
                    return (false, UiInputResult.HandledAndInvalidate);
                }

                return (true, UiInputResult.HandledResult);
            },
            (_, complete) =>
            {
                events.Add($"domain:{state}");
                return complete ? ModalDialogLoopResult<int>.Complete(state) : ModalDialogLoopResult<int>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                events.Add($"apply:{frame}");
                state = frame;
            });

        Assert.Equal(1, result);
        Assert.Equal(
            ["render:0", "apply:0", "route", "domain:1", "render:1", "apply:1", "route", "domain:1"],
            events);
    }

    [Fact]
    public void RunInteractive_PostDispatchFocusRequestCommitsWithNextFrame()
    {
        var driver = new FakeConsoleDriver(20, 5);
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out _);
        var first = new UiTargetId("first");
        var requested = new UiTargetId("requested");
        var routedTargets = new List<UiTargetId?>();
        int renders = 0;

        int result = modals.RunInteractive<int, bool, int>(
            (context, focusScope) =>
            {
                renders++;
                if (renders == 2)
                {
                    driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
                    driver.ResizeAfterWrite = current => current.SetSize(21, 5);
                }
                context.Canvas.Write(0, 0, "M", Style);
                return renders;
            },
            _ => new UiInteractionFrame(
                [],
                FocusFrame([new UiFocusEntry(first, 0), new UiFocusEntry(requested, 1)], first)),
            (input, _, route) =>
            {
                routedTargets.Add(route.Target);
                return (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }, UiInputResult.HandledResult);
            },
            (_, complete) => complete
                ? ModalDialogLoopResult<int>.Complete(42)
                : ModalDialogLoopResult<int>.ContinueWithFocus(requested));

        Assert.Equal(42, result);
        Assert.Equal([first, requested], routedTargets);
        Assert.True(renders >= 2);
    }

    [Fact]
    public void RunInteractiveTimed_WakeUsesCommittedFrameInvalidatesAndCompletes()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var modals = CreateHost(driver, out var composition);
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
            context.Canvas.Write(0, 0, "R", Style)));
        var applied = new List<ConsoleSize>();
        var wakeFrames = new List<ConsoleSize>();
        int wakes = 0;
        bool changed = false;

        int result = modals.RunInteractiveTimed<ConsoleSize, string, int>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, changed ? "W" : "M", Style);
                return context.Size;
            },
            _ => UiInteractionFrame.Empty,
            (_, _, _) => ("input", UiInputResult.HandledResult),
            (_, _) => ModalDialogLoopResult<int>.Complete(-1),
            getNextWakeUtc: () => DateTimeOffset.UtcNow,
            handleWake: frame =>
            {
                wakeFrames.Add(frame);
                wakes++;
                if (wakes == 1)
                {
                    changed = true;
                    driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
                    driver.ResizeAfterWrite = d => d.SetSize(100, 35);
                    return ModalDialogWakeResult<int>.Changed;
                }

                return ModalDialogWakeResult<int>.Complete(42);
            },
            applyCommittedFrame: applied.Add);

        Assert.Equal(42, result);
        Assert.Equal([new ConsoleSize(80, 25), new ConsoleSize(100, 35)], wakeFrames);
        Assert.Equal(new ConsoleSize(100, 35), applied[^1]);
        Assert.DoesNotContain(new ConsoleSize(80, 25), applied.Skip(1));
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void RunInteractiveTimed_ExternalWakeLeavesUnreadInputForParentSurface()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var modals = CreateHost(driver, out _);
        using var wake = new CancellationTokenSource();
        wake.Cancel();
        int routed = 0;

        int result = modals.RunInteractiveTimed<int, string, int>(
            (context, _) =>
            {
                context.Canvas.Write(0, 0, "M", Style);
                return 1;
            },
            _ => UiInteractionFrame.Empty,
            (_, _, _) =>
            {
                routed++;
                return ("input", UiInputResult.HandledResult);
            },
            (_, _) => ModalDialogLoopResult<int>.Complete(-1),
            getNextWakeUtc: () => DateTimeOffset.UtcNow.AddMinutes(1),
            handleWake: _ => ModalDialogWakeResult<int>.Complete(42),
            wakeSignal: wake.Token);

        Assert.Equal(42, result);
        Assert.Equal(0, routed);
        Assert.Equal(ConsoleKey.F10, driver.ReadKey(intercept: true).Key);
    }

    [Fact]
    public void RunRouted_HandlerExceptionDisposesOverlayAndRestoresSurface()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", Style);
        }));

        Assert.Throws<InvalidOperationException>(() => modals.RunRouted<int, int>(
            _ => 1,
            _ => throw new InvalidOperationException("handler")));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void RunRouted_CancellationDisposesOverlayAndRestoresSurface()
    {
        var driver = new FakeConsoleDriver();
        var modals = CreateHost(driver, out var composition);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, context =>
        {
            rootRenders++;
            context.Canvas.Write(0, 0, "R", Style);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => modals.RunRouted<int, int>(
            _ => 1,
            _ => ModalDialogLoopResult<int>.Complete(0),
            cancellationToken: cancellation.Token));

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
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
        public IUiFocusState FocusState { get; } = new UiFocusController();
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
