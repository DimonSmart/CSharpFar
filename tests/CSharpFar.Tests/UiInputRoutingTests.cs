using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiInputRoutingTests
{
    [Fact]
    public void DispatchInput_RoutesTopmostOverlayFirstAndBubbles()
    {
        var (host, surface) = Host();
        var bottom = new RecordingLayer(UiLayerInputPolicy.Bubble, "bottom");
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top");
        using var bottomScope = host.PushOverlay(bottom);
        using var topScope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Equal(["top", "bottom", "surface"], top.Calls.Concat(bottom.Calls).Concat(surface.Calls));
    }

    [Fact]
    public void DispatchInput_HandledBubbleStopsLowerLayers()
    {
        var (host, surface) = Host();
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top")
        {
            Result = UiInputResult.HandledResult,
        };
        using var scope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.Equal(["top"], top.Calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_ModalStopsEvenWhenUnhandled()
    {
        var (host, surface) = Host();
        var modal = new RecordingLayer(UiLayerInputPolicy.Modal, "modal");
        using var scope = host.PushOverlay(modal);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_SkipsNoneLayersAndResize()
    {
        var (host, surface) = Host();
        var none = new RecordingLayer(UiLayerInputPolicy.None, "none");
        using var scope = host.PushOverlay(none);

        Assert.False(host.DispatchInput(new ConsoleResizeInputEvent()).Handled);
        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Empty(none.Calls);
        Assert.Equal(["surface"], surface.Calls);
    }

    [Fact]
    public void DispatchInput_AppliesFocusRequestToSourceLayerOnlyAndNormalizesResult()
    {
        var (host, surface) = Host();
        var target = new UiTargetId("target");
        surface.FocusScope.Commit(new UiFocusFrame([new(target, 0)]));
        surface.Result = UiInputResult.RequestFocus(target);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal(UiFocusRequest.None, result.FocusRequest);
        Assert.Equal(target, surface.FocusScope.FocusedTarget);
    }

    private static (UiCompositionHost Host, RecordingLayer Surface) Host()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var surface = new RecordingLayer(UiLayerInputPolicy.Bubble, "surface");
        host.SetRootSurface(new SurfaceLayer(host.Screen, surface));
        return (host, surface);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private sealed class RecordingLayer(UiLayerInputPolicy policy, string name) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public UiFocusScope FocusScope { get; } = new();
        public List<string> Calls { get; } = [];
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            Calls.Add(name);
            return Result;
        }
    }

    private sealed class SurfaceLayer(ScreenRenderer screen, RecordingLayer layer) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => layer.InputPolicy;
        public UiFocusScope FocusScope => layer.FocusScope;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) => layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);
    }
}
