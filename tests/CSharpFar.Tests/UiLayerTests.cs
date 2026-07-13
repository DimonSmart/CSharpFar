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
        Assert.Same(layer.CommittedInteractionFrame.Focus, layer.FocusScope.CurrentFrame);
        Assert.Equal(new UiTargetId("target"), layer.FocusScope.FocusedTarget);
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
            layer.RouteInput(Key(ConsoleKey.A), new UiInputRouteContext(layer.FocusScope, null, false)));
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
                context.Screen.Write(0, 0, (marker++).ToString(), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return new TestFrame(context.Viewport.Width, new UiFocusFrame([
                    new(new UiTargetId($"target-{context.Viewport.Width}"), 0),
                ]));
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));

        host.Render();

        Assert.Equal(100, layer.CommittedFrame.Value);
        Assert.Same(layer.CommittedInteractionFrame.Focus, layer.FocusScope.CurrentFrame);
        Assert.Equal(new UiTargetId("target-100"), layer.FocusScope.FocusedTarget);
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
        Assert.Equal(new UiTargetId("target-80"), layer.FocusScope.FocusedTarget);
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
                context.Screen.Write(0, 0, value.ToString(), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
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

    private static UiCompositionHost Host(TestLayer layer)
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));
        return host;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private sealed record TestFrame(int Value, UiFocusFrame Focus);

    private sealed class TestLayer(UiLayerInputPolicy policy) : UiLayer<TestFrame>
    {
        public Func<UiRenderContext, TestFrame> RenderCore { get; set; } =
            _ => new TestFrame(0, UiFocusFrame.Empty);

        public List<int> CommittedValues { get; } = [];
        public List<(int FrameValue, bool InteractionCommitted, UiTargetId? FocusedTarget)> CommitSnapshots { get; } = [];

        public override UiLayerInputPolicy InputPolicy => policy;

        protected override TestFrame RenderFrame(UiRenderContext context) =>
            RenderCore(context);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            TestFrame frame,
            UiInputRouteContext context) =>
            RouteCore(input, frame, context);

        protected override UiInteractionFrame BuildInteractionFrame(TestFrame frame) =>
            new([], frame.Focus);

        protected override void OnFrameCommitted(TestFrame frame) =>
            Commit(frame);

        private void Commit(TestFrame frame)
        {
            CommittedValues.Add(frame.Value);
            CommitSnapshots.Add((
                CommittedFrame.Value,
                ReferenceEquals(CommittedInteractionFrame.Focus, FocusScope.CurrentFrame),
                FocusScope.FocusedTarget));
        }

        public int LastInputFrameValue(ConsoleInputEvent input)
        {
            int value = -1;
            RouteCore = (_, frame, _) =>
            {
                value = frame.Value;
                return UiInputResult.NotHandled;
            };
            RouteInput(input, new UiInputRouteContext(FocusScope, null, false));
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
        public UiFocusScope FocusScope => _layer.FocusScope;
        public UiInteractionFrame CommittedInteractionFrame => _layer.CommittedInteractionFrame;
        public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
        public void Render(UiRenderContext context) => _layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            _layer.RouteInput(input, context);
    }
}
