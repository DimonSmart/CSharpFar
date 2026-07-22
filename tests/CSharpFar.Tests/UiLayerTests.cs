using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiLayerTests
{
    [Fact]
    public void StableRender_PublishesFrameFocusAndCallback()
    {
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context => new TestFrame(context.Viewport.Width, new UiFocusFrame([
                new(new UiTargetId("target"), 0),
            ])),
        };
        var host = Host(layer);

        host.Render();

        Assert.True(layer.HasCommittedFrame);
        Assert.Equal(80, layer.CommittedFrame.Value);
        Assert.Same(layer.CommittedInteractionFrame.Focus, layer.FocusState.CurrentFrame);
        Assert.Equal(new UiTargetId("target"), layer.FocusState.FocusedTarget);
        Assert.Equal([80], layer.CommittedValues);
    }

    [Fact]
    public void BeforeFirstRender_CommittedInteractionFrameIsEmpty()
    {
        var layer = new TestLayer(UiLayerInputPolicy.Bubble);

        Assert.Same(UiInteractionFrame.Empty, layer.CommittedInteractionFrame);
    }

    [Fact]
    public void StableRender_DefaultInteractionFrameIsEmpty()
    {
        var layer = new DefaultInteractionLayer();
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));

        host.Render();

        Assert.Same(UiInteractionFrame.Empty, layer.CommittedInteractionFrame);
    }

    [Fact]
    public void StableRender_PublishesCursorThroughCommittedFocus()
    {
        var cursor = new UiCursorPlacement(12, 7, Visible: true);
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, new UiFocusFrame([
                new(new UiTargetId("target"), 0, Cursor: cursor),
            ])),
        };
        var host = Host(layer);

        host.Render();

        Assert.Equal(cursor, Assert.Single(layer.CommittedInteractionFrame.Focus.Entries).Cursor);
    }

    [Fact]
    public void StableRender_AppliesCursorInsideViewportProtectedFrame()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, new UiFocusFrame([
                new(new UiTargetId("target"), 0, Cursor: new UiCursorPlacement(12, 7)),
            ])),
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));

        host.Render();

        Assert.Equal(1, driver.TrySetCursorPositionInViewportCallCount);
        Assert.Equal(0, driver.SetCursorPositionCallCount);
        Assert.Equal((12, 7), (driver.CursorX, driver.CursorY));
        Assert.True(driver.CursorVisible);
    }

    [Fact]
    public void StableRender_TopmostCursorlessFocusHidesLowerLayerCursor()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var root = CursorLayer("root", new UiCursorPlacement(1, 1));
        var middle = CursorLayer("middle", new UiCursorPlacement(2, 2));
        var top = CursorLayer("top", null);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, root));
        using var middleScope = host.RegisterOverlay(middle);
        using var topScope = host.RegisterOverlay(top);

        host.Render();

        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void StableRender_TopmostVisibleFocusOwnsCursorPosition()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var root = CursorLayer("root", new UiCursorPlacement(1, 1));
        var top = CursorLayer("top", new UiCursorPlacement(3, 4));
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, root));
        using var topScope = host.RegisterOverlay(top);

        host.Render();

        Assert.True(driver.CursorVisible);
        Assert.Equal((3, 4), (driver.CursorX, driver.CursorY));
    }

    [Fact]
    public void ResizeDuringCursorPosition_RejectsAttemptBeforeCommittingAndRetries()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context => new TestFrame(context.Viewport.Width, new UiFocusFrame([
                new(new UiTargetId($"target-{context.Viewport.Width}"), 0,
                    Cursor: new UiCursorPlacement(context.Viewport.Width - 1, 0)),
            ])),
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));
        host.Render();
        var cursor = (driver.CursorVisible, driver.CursorX, driver.CursorY);
        long stableVersion = host.StableRenderVersion;
        bool observedRejectedAttempt = false;
        driver.BeforeTrySetCursorPositionInViewport = current =>
        {
            observedRejectedAttempt = true;
            Assert.Equal(80, layer.CommittedFrame.Value);
            Assert.Equal(cursor, (current.CursorVisible, current.CursorX, current.CursorY));
            current.SetSize(100, 35);
            current.BeforeTrySetCursorPositionInViewport = null;
        };

        host.Render();

        Assert.True(observedRejectedAttempt);
        Assert.Equal(stableVersion + 1, host.StableRenderVersion);
        Assert.Equal(100, layer.CommittedFrame.Value);
        Assert.Equal((99, 0), (driver.CursorX, driver.CursorY));
        Assert.True(driver.CursorVisible);
    }

    [Fact]
    public void RejectedRender_DoesNotApplyCursorlessFocusUntilRetryCommits()
    {
        var driver = new FakeConsoleDriver(80, 25);
        bool cursorless = false;
        var root = CursorLayer("root", new UiCursorPlacement(1, 1));
        var top = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context =>
            {
                context.Canvas.Write(0, 0, cursorless ? "B" : "A", CellStyle.Default);
                return new TestFrame(1, cursorless
                    ? new UiFocusFrame([new(new UiTargetId("top"), 0)])
                    : UiFocusFrame.Empty);
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, root));
        using var topScope = host.RegisterOverlay(top);
        host.Render();
        var cursor = (driver.CursorVisible, driver.CursorX, driver.CursorY);
        cursorless = true;
        bool observedRejectedAttempt = false;
        driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
        driver.ResizeAfterWrite = current => current.SetSize(100, 35);
        driver.BeforeViewportWrite = current =>
        {
            observedRejectedAttempt = true;
            Assert.Equal(cursor, (current.CursorVisible, current.CursorX, current.CursorY));
            Assert.Empty(top.CommittedInteractionFrame.Focus.Entries);
            current.BeforeViewportWrite = null;
        };

        host.Render();

        Assert.True(observedRejectedAttempt);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void OnFrameCommitted_SeesCommittedFrameInteractionAndFocus()
    {
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context => new TestFrame(context.Viewport.Width, new UiFocusFrame([
                new(new UiTargetId("target"), 0),
            ])),
        };
        var host = Host(layer);

        host.Render();

        Assert.Equal([(80, true, new UiTargetId("target"))], layer.CommitSnapshots);
    }

    [Fact]
    public void RouteInput_BeforeCommittedRenderThrows()
    {
        var layer = new TestLayer(UiLayerInputPolicy.Bubble);

        Assert.Throws<InvalidOperationException>(() =>
            layer.RouteInput(Key(ConsoleKey.A), UiInputRouteContext.Layer(layer.FocusState)));
    }

    [Fact]
    public void RejectedRender_DoesNotPublishFrameFocusOrCallback()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        char marker = 'a';
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context =>
            {
                context.Canvas.Write(0, 0, (marker++).ToString(), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return new TestFrame(context.Viewport.Width, new UiFocusFrame([
                    new(new UiTargetId($"target-{context.Viewport.Width}"), 0),
                ]));
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));

        host.Render();

        Assert.Equal(100, layer.CommittedFrame.Value);
        Assert.Same(layer.CommittedInteractionFrame.Focus, layer.FocusState.CurrentFrame);
        Assert.Equal(new UiTargetId("target-100"), layer.FocusState.FocusedTarget);
        Assert.Equal([100], layer.CommittedValues);
        Assert.Equal(100, layer.LastInputFrameValue(Key(ConsoleKey.A)));
    }

    [Fact]
    public void FailedRenderAfterStableFrame_KeepsPreviousInteractionAndFocus()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context =>
            {
                return new TestFrame(context.Viewport.Width, new UiFocusFrame([
                    new(new UiTargetId($"target-{context.Viewport.Width}"), 0),
                ]));
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));
        host.Render();
        var previousInteraction = layer.CommittedInteractionFrame;
        layer.RenderCore = _ => throw new InvalidOperationException("render failed");

        Assert.Throws<InvalidOperationException>(() => host.Render());

        Assert.Equal(80, layer.CommittedFrame.Value);
        Assert.Same(previousInteraction, layer.CommittedInteractionFrame);
        Assert.Equal(new UiTargetId("target-80"), layer.FocusState.FocusedTarget);
        Assert.Equal([80], layer.CommittedValues);
    }

    [Fact]
    public void RejectedResizeAttempt_DoesNotPublishCursorMetadata()
    {
        var driver = new FakeConsoleDriver(80, 25);
        int attempt = 0;
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context =>
            {
                int value = ++attempt;
                context.Canvas.Write(0, 0, value.ToString(), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return new TestFrame(value, new UiFocusFrame([
                    new(new UiTargetId($"target-{value}"), 0, Cursor: new UiCursorPlacement(value, 0)),
                ]));
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));
        host.Render();
        driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
        driver.ResizeAfterWrite = d => d.SetSize(100, 35);

        host.Render();

        Assert.Equal([1, 3], layer.CommittedValues);
        Assert.Equal(new UiCursorPlacement(3, 0),
            Assert.Single(layer.CommittedInteractionFrame.Focus.Entries).Cursor);
        Assert.DoesNotContain(2, layer.CommittedValues);
    }

    [Fact]
    public void RouteInput_UsesCommittedFocusedTargetForKeyboard()
    {
        var first = new UiTargetId("first");
        var second = new UiTargetId("second");
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, new UiFocusFrame([new(first, 0), new(second, 1)], first)),
        };
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));

        Assert.Equal(first, layer.LastRoute!.Target);
        Assert.Equal(UiInputRouteKind.FocusedTarget, layer.LastRoute.RouteKind);
    }

    [Fact]
    public void RouteInput_UsesTopmostCommittedHitTargetForMouse()
    {
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, UiFocusFrame.Empty),
            HitRegions = [
                new(new UiTargetId("bottom"), new Rect(0, 0, 4, 4)),
                new(new UiTargetId("top"), new Rect(0, 0, 4, 4)),
            ],
        };
        var host = Host(layer);
        host.Render();

        host.DispatchInput(new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        Assert.Equal(new UiTargetId("top"), layer.LastRoute!.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, layer.LastRoute.RouteKind);
    }

    [Fact]
    public void FocusRequest_AffectsOnlyTheNextRoute()
    {
        var first = new UiTargetId("first");
        var second = new UiTargetId("second");
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, new UiFocusFrame([new(first, 0), new(second, 1)], first)),
            RouteCore = (_, _, context) => context.Target == first
                ? UiInputResult.RequestFocus(second)
                : UiInputResult.NotHandled,
        };
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        Assert.Equal(first, layer.LastRoute!.Target);
        host.DispatchInput(Key(ConsoleKey.B));

        Assert.Equal(second, layer.LastRoute!.Target);
    }

    [Fact]
    public void RemovedCommittedTarget_ClearsCaptureAndRoutesCurrentMouseNormally()
    {
        var target = new UiTargetId("thumb");
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, UiFocusFrame.Empty),
            HitRegions = [new(target, new Rect(0, 0, 2, 2))],
            RouteCore = (input, _, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
                ? UiInputResult.CaptureMouse(target, MouseButton.Left)
                : UiInputResult.NotHandled,
        };
        var host = Host(layer);
        host.Render();
        host.DispatchInput(new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        layer.HitRegions = [];
        host.Render();
        host.DispatchInput(new MouseConsoleInputEvent(10, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.Equal(UiInputRouteKind.Layer, layer.LastRoute!.RouteKind);
        Assert.Null(layer.LastRoute.Target);
    }

    private static UiCompositionHost Host(TestLayer layer)
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));
        return host;
    }

    private static TestLayer CursorLayer(string target, UiCursorPlacement? cursor) =>
        new(UiLayerInputPolicy.Bubble)
        {
            RenderCore = _ => new TestFrame(1, new UiFocusFrame([
                new(new UiTargetId(target), 0, Cursor: cursor),
            ])),
        };

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private sealed record TestFrame(int Value, UiFocusFrame Focus);

    private sealed class TestLayer(UiLayerInputPolicy policy) : UiLayer<TestFrame>
    {
        public Func<UiRenderContext, TestFrame> RenderCore { get; set; } =
            _ => new TestFrame(0, UiFocusFrame.Empty);

        public List<int> CommittedValues { get; } = [];
        public List<(int FrameValue, bool InteractionCommitted, UiTargetId? FocusedTarget)> CommitSnapshots { get; } = [];
        public UiInputRouteContext? LastRoute { get; private set; }
        public IReadOnlyList<UiHitRegion> HitRegions { get; set; } = [];

        public override UiLayerInputPolicy InputPolicy => policy;

        protected override TestFrame RenderFrame(UiRenderContext context) =>
            RenderCore(context);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            TestFrame frame,
            UiInputRouteContext context)
        {
            LastRoute = context;
            return RouteCore(input, frame, context);
        }

        protected override UiInteractionFrame BuildInteractionFrame(TestFrame frame) =>
            new(HitRegions, frame.Focus);

        protected override void OnFrameCommitted(TestFrame frame) =>
            Commit(frame);

        private void Commit(TestFrame frame)
        {
            CommittedValues.Add(frame.Value);
            CommitSnapshots.Add((
                CommittedFrame.Value,
                ReferenceEquals(CommittedInteractionFrame.Focus, FocusState.CurrentFrame),
                FocusState.FocusedTarget));
        }

        public int LastInputFrameValue(ConsoleInputEvent input)
        {
            int value = -1;
            RouteCore = (_, frame, _) =>
            {
                value = frame.Value;
                return UiInputResult.NotHandled;
            };
            RouteInput(input, UiInputRouteContext.Layer(FocusState));
            return value;
        }

        public Func<ConsoleInputEvent, TestFrame, UiInputRouteContext, UiInputResult> RouteCore { get; set; } =
            (_, _, _) => UiInputResult.NotHandled;
    }

    private sealed class DefaultInteractionLayer : UiLayer<TestFrame>
    {
        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

        protected override TestFrame RenderFrame(UiRenderContext context) =>
            new(context.Viewport.Width, UiFocusFrame.Empty);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            TestFrame frame,
            UiInputRouteContext context) => UiInputResult.NotHandled;
    }

    private sealed class SurfaceLayer : IUiSurface, IUiLayer
    {
        private readonly ScreenRenderer _screen;
        private readonly UiLayer<TestFrame> _layer;

        public SurfaceLayer(ScreenRenderer screen, UiLayer<TestFrame> layer) =>
            (_screen, _layer) = (screen, layer);

        public UiLayerInputPolicy InputPolicy => _layer.InputPolicy;
        public IUiFocusState FocusState => _layer.FocusState;
        public UiInteractionFrame CommittedInteractionFrame => _layer.CommittedInteractionFrame;
        public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
        public void Render(UiRenderContext context) => _layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            _layer.RouteInput(input, context);
    }
}
