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
        var (host, surface) = Fixture(calls);
        var bottom = new RecordingLayer(UiLayerInputPolicy.Bubble, "bottom", calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var bottomScope = host.PushOverlay(bottom);
        using var topScope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Equal(["top", "bottom", "surface"], calls);
    }

    [Fact]
    public void DispatchInput_HandledBubbleStopsLowerLayers()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls)
        {
            Result = UiInputResult.HandledResult,
        };
        using var scope = host.PushOverlay(top);

        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.Equal(["top"], calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_ModalStopsEvenWhenUnhandled()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        var modal = new RecordingLayer(UiLayerInputPolicy.Modal, "modal", calls);
        using var scope = host.PushOverlay(modal);

        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Equal(["modal"], calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void DispatchInput_TemporarySurfaceReplacesRoot()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var temporary = new RecordingLayer(UiLayerInputPolicy.Bubble, "temporary", calls);
        using var scope = host.OpenSurface(new UiLayerTestSurface(host.Screen, temporary));

        host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.Equal(["temporary"], calls);
    }

    [Fact]
    public void DispatchInput_OverlaysBelowActiveTemporarySurfaceDoNotParticipate()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var bottom = new RecordingLayer(UiLayerInputPolicy.Bubble, "bottom", calls);
        var temporary = new RecordingLayer(UiLayerInputPolicy.Bubble, "temporary", calls);
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var bottomScope = host.PushOverlay(bottom);
        using var temporaryScope = host.OpenSurface(new UiLayerTestSurface(host.Screen, temporary));
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.Equal(["top", "temporary"], calls);
    }

    [Fact]
    public void DispatchInput_CaptureOwnerReceivesCapturedEventBeforeUpperBubbleLayers()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var owner = new RecordingLayer(UiLayerInputPolicy.Bubble, "owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new RecordingLayer(UiLayerInputPolicy.Bubble, "top", calls);
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down));
        calls.Clear();
        owner.Result = UiInputResult.NotHandled;
        top.Result = UiInputResult.HandledResult;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move));

        Assert.Equal(["owner"], calls);
        Assert.Contains(owner.Contexts, context => context.IsCapturedRoute);
    }

    [Fact]
    public void DispatchInput_SkipsNoneLayersAndResize()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        var none = new RecordingLayer(UiLayerInputPolicy.None, "none", calls);
        using var scope = host.PushOverlay(none);

        Assert.False(host.DispatchInput(new ConsoleResizeInputEvent()).Handled);
        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.False(result.Handled);
        Assert.Empty(none.Calls);
        Assert.Equal(["surface"], surface.Calls);
    }

    [Fact]
    public void DispatchInput_AppliesFocusRequestToSourceLayerOnlyAndNormalizesResult()
    {
        var (host, surface) = Fixture([]);
        var target = new UiTargetId("target");
        ((UiFocusController)surface.FocusState).Commit(FocusFrame([new(target, 0)]));
        surface.Result = UiInputResult.RequestFocus(target);

        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal(UiFocusRequest.None, result.FocusRequest);
        Assert.Equal(target, surface.FocusState.FocusedTarget);
    }

    private static (UiCompositionHost Host, RecordingLayer Surface) Fixture(List<string> calls)
    {
        var surface = new RecordingLayer(UiLayerInputPolicy.Bubble, "surface", calls);
        var fixture = new UiLayerTestHost(surface);
        return (fixture.Composition, surface);
    }

    private sealed class RecordingLayer(UiLayerInputPolicy policy, string name, List<string> calls) : IUiLayer, IUiFocusRuntime
    {
        private readonly UiFocusController _focus = new();

        public UiLayerInputPolicy InputPolicy => policy;
        public IUiFocusState FocusState => _focus;
        public UiInteractionFrame CommittedInteractionFrame { get; } = new([
            new(new UiTargetId("thumb"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
        ]);
        public List<string> Calls { get; } = [];
        public List<UiInputRouteContext> Contexts { get; } = [];
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }

        void IUiFocusRuntime.RequestFocusOnNextCommit(UiFocusRequest request) =>
            _focus.RequestOnNextCommit(request);

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            Calls.Add(name);
            Contexts.Add(context);
            calls.Add(name);
            return Result;
        }
    }

}
