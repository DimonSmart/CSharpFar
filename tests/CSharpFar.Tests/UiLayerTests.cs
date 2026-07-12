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
        Assert.Equal(new UiTargetId("target"), layer.FocusScope.FocusedTarget);
        Assert.Equal([80], layer.CommittedValues);
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
        var layer = new TestLayer(UiLayerInputPolicy.Bubble)
        {
            RenderCore = context =>
            {
                context.Screen.Write(0, 0, "x", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return new TestFrame(context.Viewport.Width, new UiFocusFrame([
                    new(new UiTargetId($"target-{context.Viewport.Width}"), 0),
                ]));
            },
        };
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new SurfaceLayer(host.Screen, layer));

        host.Render();

        Assert.Equal(100, layer.CommittedFrame.Value);
        Assert.Equal(new UiTargetId("target-100"), layer.FocusScope.FocusedTarget);
        Assert.Equal([100], layer.CommittedValues);
        Assert.Equal(100, layer.LastInputFrameValue(Key(ConsoleKey.A)));
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

        public override UiLayerInputPolicy InputPolicy => policy;

        protected override TestFrame RenderFrame(UiRenderContext context) =>
            RenderCore(context);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            TestFrame frame,
            UiInputRouteContext context) =>
            RouteCore(input, frame, context);

        protected override UiFocusFrame BuildFocusFrame(TestFrame frame) =>
            frame.Focus;

        protected override void OnFrameCommitted(TestFrame frame) =>
            CommittedValues.Add(frame.Value);

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

    private sealed class SurfaceLayer : IUiSurface, IUiLayer
    {
        private readonly ScreenRenderer _screen;
        private readonly TestLayer _layer;

        public SurfaceLayer(ScreenRenderer screen, TestLayer layer) =>
            (_screen, _layer) = (screen, layer);

        public UiLayerInputPolicy InputPolicy => _layer.InputPolicy;
        public UiFocusScope FocusScope => _layer.FocusScope;
        public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
        public void Render(UiRenderContext context) => _layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            _layer.RouteCore(input, _layer.CommittedFrame, context);
    }
}
