using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiMouseCaptureTests
{
    [Fact]
    public void Capture_RoutesLaterMouseEventsOnlyToOwnerWithCapturedContext()
    {
        var (host, surface) = Host();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Mouse(MouseEventKind.Down, MouseButton.Left));
        owner.Result = UiInputResult.NotHandled;
        top.Result = UiInputResult.HandledResult;
        host.DispatchInput(Mouse(MouseEventKind.Move, MouseButton.Left));

        Assert.Contains(owner.Contexts, context => context.IsCapturedRoute && context.CapturedTarget == new UiTargetId("thumb"));
        Assert.Equal(2, owner.Calls.Count);
        Assert.Single(top.Calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void MatchingUp_ReleasesCapture()
    {
        var (host, _) = Host();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Mouse(MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        owner.Result = UiInputResult.NotHandled;
        host.DispatchInput(Mouse(MouseEventKind.Up, MouseButton.Left));
        host.DispatchInput(Mouse(MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(2, owner.Calls.Count);
        Assert.Equal(2, top.Calls.Count);
    }

    [Fact]
    public void NonMatchingUp_DoesNotReleaseCapture()
    {
        var (host, _) = Host();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Mouse(MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        owner.Result = UiInputResult.NotHandled;
        host.DispatchInput(Mouse(MouseEventKind.Up, MouseButton.Right));
        host.DispatchInput(Mouse(MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(3, owner.Calls.Count);
        Assert.Single(top.Calls);
    }

    [Fact]
    public void DisposeOwner_ClearsCapture()
    {
        var (host, _) = Host();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(Mouse(MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        topScope.Dispose();
        ownerScope.Dispose();
        host.DispatchInput(Mouse(MouseEventKind.Move, MouseButton.Left));

        Assert.Single(owner.Calls);
    }

    [Fact]
    public void InvalidCaptureRequestsThrow()
    {
        var (host, surface) = Host();
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left);

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(Key(ConsoleKey.A)));
        Assert.Throws<ArgumentException>(() => UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.WheelUp));
    }

    private static (UiCompositionHost Host, CaptureLayer Surface) Host()
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        var surface = new CaptureLayer("surface");
        host.SetRootSurface(new SurfaceLayer(host.Screen, surface));
        return (host, surface);
    }

    private static MouseConsoleInputEvent Mouse(MouseEventKind kind, MouseButton button) =>
        new(1, 1, button, kind, MouseKeyModifiers.None);

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private sealed class CaptureLayer(string name) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public UiFocusScope FocusScope { get; } = new();
        public List<string> Calls { get; } = [];
        public List<UiInputRouteContext> Contexts { get; } = [];
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            Calls.Add(name);
            Contexts.Add(context);
            return Result;
        }
    }

    private sealed class SurfaceLayer(ScreenRenderer screen, CaptureLayer layer) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => layer.InputPolicy;
        public UiFocusScope FocusScope => layer.FocusScope;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) => layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);
    }
}
