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
        var calls = new List<string>();
        var (host, surface) = Host(calls);
        var bottom = new RecordingLayer(UiLayerInputPolicy.Bubble, "bottom", calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var bottomScope = host.PushOverlay(bottom);
        using var topScope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Equal(["top", "bottom", "surface"], calls);
    }

    [Fact]
    public void DispatchInput_HandledBubbleStopsLowerLayers()
    {
        var calls = new List<string>();
        var (host, surface) = Host(calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls)
        {
            Result = UiInputResult.HandledResult,
        };
        using var scope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.Equal(["top"], calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_ModalStopsEvenWhenUnhandled()
    {
        var calls = new List<string>();
        var (host, surface) = Host(calls);
        var modal = new RecordingLayer(UiLayerInputPolicy.Modal, "modal", calls);
        using var scope = host.PushOverlay(modal);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Equal(["modal"], calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_TemporarySurfaceReplacesRoot()
    {
        var calls = new List<string>();
        var (host, _) = Host(calls);
        var temporary = new RecordingLayer(UiLayerInputPolicy.Bubble, "temporary", calls);
        using var scope = host.OpenSurface(new SurfaceLayer(host.Screen, temporary));

        host.DispatchInput(Key(ConsoleKey.A));

        Assert.Equal(["temporary"], calls);
    }

    [Fact]
    public void DispatchInput_OverlaysBelowActiveTemporarySurfaceDoNotParticipate()
    {
        var calls = new List<string>();
        var (host, _) = Host(calls);
        var bottom = new RecordingLayer(UiLayerInputPolicy.Bubble, "bottom", calls);
        var temporary = new RecordingLayer(UiLayerInputPolicy.Bubble, "temporary", calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var bottomScope = host.PushOverlay(bottom);
        using var temporaryScope = host.OpenSurface(new SurfaceLayer(host.Screen, temporary));
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Key(ConsoleKey.A));

        Assert.Equal(["top", "temporary"], calls);
    }

    [Fact]
    public void DispatchInput_CaptureOwnerReceivesCapturedEventBeforeUpperBubbleLayers()
    {
        var calls = new List<string>();
        var (host, _) = Host(calls);
        var owner = new RecordingLayer(UiLayerInputPolicy.Bubble, "owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Mouse(MouseEventKind.Down, MouseButton.Left));
        calls.Clear();
        owner.Result = UiInputResult.NotHandled;
        top.Result = UiInputResult.HandledResult;
        host.DispatchInput(Mouse(MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["owner"], calls);
        Assert.Contains(owner.Contexts, context => context.IsCapturedRoute);
    }

    [Fact]
    public void DispatchInput_SkipsNoneLayersAndResize()
    {
        var calls = new List<string>();
        var (host, surface) = Host(calls);
        var none = new RecordingLayer(UiLayerInputPolicy.None, "none", calls);
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
        var (host, surface) = Host([]);
        var target = new UiTargetId("target");
        surface.FocusState.Commit(new UiFocusFrame([new(target, 0)]));
        surface.Result = UiInputResult.RequestFocus(target);

        UiInputResult result = host.DispatchInput(Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal(UiFocusRequest.None, result.FocusRequest);
        Assert.Equal(target, surface.FocusState.FocusedTarget);
    }

    private static (UiCompositionHost Host, RecordingLayer Surface) Host(List<string> calls)
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var surface = new RecordingLayer(UiLayerInputPolicy.Bubble, "surface", calls);
        host.SetRootSurface(new SurfaceLayer(host.Screen, surface));
        return (host, surface);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static MouseConsoleInputEvent Mouse(MouseEventKind kind, MouseButton button) =>
        new(1, 1, button, kind, MouseKeyModifiers.None);

    private sealed class RecordingLayer(UiLayerInputPolicy policy, string name, List<string> calls) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public IUiFocusState FocusState { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame { get; } = new([
            new(new UiTargetId("thumb"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
        ]);
        public List<string> Calls { get; } = [];
        public List<UiInputRouteContext> Contexts { get; } = [];
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            Calls.Add(name);
            Contexts.Add(context);
            calls.Add(name);
            return Result;
        }
    }

    private sealed class SurfaceLayer(ScreenRenderer screen, RecordingLayer layer) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => layer.InputPolicy;
        public IUiFocusState FocusState => layer.FocusState;
        public UiInteractionFrame CommittedInteractionFrame => layer.CommittedInteractionFrame;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) => layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);
    }
}
