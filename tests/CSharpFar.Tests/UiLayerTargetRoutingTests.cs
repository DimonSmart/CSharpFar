using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

/// <summary>Contract coverage for target resolution performed by UiLayer&lt;TFrame&gt;.</summary>
public sealed class UiLayerTargetRoutingTests
{
    [Fact]
    public void KeyboardAndModifier_UseCommittedFocusedTarget_AndInvokeHandlerOnce()
    {
        var focused = new UiTargetId("focused");
        var layer = Layer(focus: Focus(focused));
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        AssertRoute(layer, focused, UiInputRouteKind.FocusedTarget);
        host.DispatchInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        AssertRoute(layer, focused, UiInputRouteKind.FocusedTarget);
        Assert.Equal(2, layer.CallCount);
    }

    [Fact]
    public void KeyboardWithoutEnabledFocus_UsesLayerRoute()
    {
        var disabled = new UiTargetId("disabled");
        var layer = Layer(focus: new UiFocusFrame([new UiFocusEntry(disabled, 0, IsEnabled: false)]));
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));

        AssertRoute(layer, null, UiInputRouteKind.Layer);
        Assert.Null(layer.FocusScope.FocusedTarget);
    }

    [Fact]
    public void MouseHitMissAndOverlaps_UseCommittedLocalRegions()
    {
        var bottom = new UiTargetId("bottom");
        var top = new UiTargetId("top");
        var layer = Layer(regions: [new(bottom, new Rect(0, 0, 4, 4)), new(top, new Rect(0, 0, 4, 4))]);
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        AssertRoute(layer, top, UiInputRouteKind.HitTarget);
        host.DispatchInput(Mouse(9, 9, MouseEventKind.Down));
        AssertRoute(layer, null, UiInputRouteKind.Layer);
    }

    [Theory]
    [InlineData(MouseEventKind.Move)]
    [InlineData(MouseEventKind.Up)]
    [InlineData(MouseEventKind.DoubleClick)]
    [InlineData(MouseEventKind.Wheel)]
    public void EveryOrdinaryMouseKind_UsesHitResolution(MouseEventKind kind)
    {
        var target = new UiTargetId("target");
        var layer = Layer(regions: [new(target, new Rect(0, 0, 3, 3))]);
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Mouse(1, 1, kind, kind == MouseEventKind.Wheel ? MouseButton.WheelUp : MouseButton.Left));

        AssertRoute(layer, target, UiInputRouteKind.HitTarget);
    }

    [Fact]
    public void MouseHit_DoesNotChangeFocus_WithoutExplicitRequest()
    {
        var focused = new UiTargetId("focused");
        var hit = new UiTargetId("hit");
        var layer = Layer(Focus(focused), [new(hit, new Rect(0, 0, 2, 2))]);
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));

        AssertRoute(layer, hit, UiInputRouteKind.HitTarget);
        Assert.Equal(focused, layer.FocusScope.FocusedTarget);
    }

    [Fact]
    public void FocusRequest_IsValidatedAndAffectsOnlyNextRouteAndSourceLayer()
    {
        var first = new UiTargetId("first");
        var second = new UiTargetId("second");
        var root = Layer(Focus(first, second));
        var overlay = Layer(Focus(first, second));
        overlay.Result = (_, context) => context.Target == first ? UiInputResult.RequestFocus(second) : UiInputResult.NotHandled;
        var host = Host(root);
        host.Render();
        using var scope = host.PushOverlay(overlay);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        AssertRoute(overlay, first, UiInputRouteKind.FocusedTarget);
        Assert.Equal(second, overlay.FocusScope.FocusedTarget);
        Assert.Equal(first, root.FocusScope.FocusedTarget);
        host.DispatchInput(Key(ConsoleKey.B));
        AssertRoute(overlay, second, UiInputRouteKind.FocusedTarget);

        overlay.Result = (_, _) => UiInputResult.RequestFocus(new UiTargetId("missing"));
        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(Key(ConsoleKey.C)));
    }

    [Fact]
    public void FocusRequest_ForDisabledTargetIsRejected()
    {
        var enabled = new UiTargetId("enabled");
        var disabled = new UiTargetId("disabled");
        var layer = Layer(new UiFocusFrame([new(enabled, 0), new(disabled, 1, IsEnabled: false)], enabled));
        layer.Result = (_, _) => UiInputResult.RequestFocus(disabled);
        var host = Host(layer);
        host.Render();

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(Key(ConsoleKey.A)));
        Assert.Equal(enabled, layer.FocusScope.FocusedTarget);
    }

    [Fact]
    public void BubbleLayers_ResolveIndependentLocalTargets_AndModalBlocksOnMiss()
    {
        var lowerTarget = new UiTargetId("lower");
        var upperTarget = new UiTargetId("upper");
        var lower = Layer(regions: [new(lowerTarget, new Rect(0, 0, 3, 3))]);
        var upper = Layer(regions: [new(upperTarget, new Rect(0, 0, 3, 3))]);
        var host = Host(lower);
        host.Render();
        using var upperScope = host.PushOverlay(upper);
        host.Render();

        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        AssertRoute(upper, upperTarget, UiInputRouteKind.HitTarget);
        AssertRoute(lower, lowerTarget, UiInputRouteKind.HitTarget);
        Assert.Equal(1, upper.CallCount);
        Assert.Equal(1, lower.CallCount);

        var modal = Layer(policy: UiLayerInputPolicy.Modal);
        using var modalScope = host.PushOverlay(modal);
        host.Render();
        host.DispatchInput(Mouse(9, 9, MouseEventKind.Down));
        AssertRoute(modal, null, UiInputRouteKind.Layer);
        Assert.Equal(1, lower.CallCount);
    }

    [Fact]
    public void HandledHitTargetOverlay_BlocksLowerLayer()
    {
        var lower = Layer(regions: [new(new UiTargetId("lower"), new Rect(0, 0, 3, 3))]);
        var upperTarget = new UiTargetId("upper");
        var upper = Layer(regions: [new(upperTarget, new Rect(0, 0, 3, 3))]);
        upper.Result = (_, _) => UiInputResult.HandledResult;
        var host = Host(lower);
        host.Render();
        using var scope = host.PushOverlay(upper);
        host.Render();

        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));

        Assert.Equal(1, upper.CallCount);
        AssertRoute(upper, upperTarget, UiInputRouteKind.HitTarget);
        Assert.Equal(0, lower.CallCount);
    }

    [Fact]
    public void Capture_HasPriority_ReleasesOnlyMatchingButton_AndUsesNormalRoutingAfterRelease()
    {
        var captured = new UiTargetId("captured");
        var other = new UiTargetId("other");
        var layer = Layer(regions: [new(captured, new Rect(0, 0, 2, 2)), new(other, new Rect(5, 5, 2, 2))]);
        layer.Result = (input, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
            ? UiInputResult.CaptureMouse(captured, MouseButton.Left) : UiInputResult.NotHandled;
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        host.DispatchInput(Mouse(6, 6, MouseEventKind.Move));
        AssertRoute(layer, captured, UiInputRouteKind.CapturedTarget);
        host.DispatchInput(Mouse(6, 6, MouseEventKind.Up, MouseButton.Right));
        AssertRoute(layer, captured, UiInputRouteKind.CapturedTarget);
        host.DispatchInput(Mouse(6, 6, MouseEventKind.Up));
        AssertRoute(layer, captured, UiInputRouteKind.CapturedTarget);
        host.DispatchInput(Mouse(6, 6, MouseEventKind.Move));
        AssertRoute(layer, other, UiInputRouteKind.HitTarget);
    }

    [Fact]
    public void Capture_IsPreservedWhenCapturedTargetBoundsChange()
    {
        var target = new UiTargetId("target");
        var layer = Layer(regions: [new(target, new Rect(0, 0, 2, 2))]);
        layer.Result = (input, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
            ? UiInputResult.CaptureMouse(target, MouseButton.Left) : UiInputResult.NotHandled;
        var host = Host(layer);
        host.Render();
        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        layer.Regions = [new(target, new Rect(5, 5, 2, 2))];
        host.Render();

        host.DispatchInput(Mouse(9, 9, MouseEventKind.Move));

        AssertRoute(layer, target, UiInputRouteKind.CapturedTarget);
    }

    [Fact]
    public void Capture_IsAllowedForFocusOnlyTarget()
    {
        var target = new UiTargetId("focus-only");
        var layer = Layer(Focus(target));
        layer.Result = (input, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
            ? UiInputResult.CaptureMouse(target, MouseButton.Left) : UiInputResult.NotHandled;
        var host = Host(layer);
        host.Render();
        host.DispatchInput(Mouse(9, 9, MouseEventKind.Down));
        host.Render();

        host.DispatchInput(Mouse(9, 9, MouseEventKind.Move));

        AssertRoute(layer, target, UiInputRouteKind.CapturedTarget);
    }

    [Fact]
    public void SuccessfulRemoval_ClearsCaptureWithoutResurrection_AndRejectedRenderDoesNot()
    {
        var target = new UiTargetId("target");
        var layer = Layer(regions: [new(target, new Rect(0, 0, 2, 2))]);
        layer.Result = (input, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
            ? UiInputResult.CaptureMouse(target, MouseButton.Left) : UiInputResult.NotHandled;
        var host = Host(layer);
        host.Render();
        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));

        layer.Regions = [];
        host.Render();
        layer.Regions = [new(target, new Rect(0, 0, 2, 2))];
        host.Render();
        host.DispatchInput(Mouse(1, 1, MouseEventKind.Move));
        AssertRoute(layer, target, UiInputRouteKind.HitTarget);
    }

    [Fact]
    public void RejectedRender_DoesNotClearCaptureFromCommittedInteractionFrame()
    {
        var target = new UiTargetId("target");
        var layer = Layer(regions: [new(target, new Rect(0, 0, 2, 2))]);
        layer.Result = (input, _) => input is MouseConsoleInputEvent { Kind: MouseEventKind.Down }
            ? UiInputResult.CaptureMouse(target, MouseButton.Left) : UiInputResult.NotHandled;
        var host = Host(layer);
        host.Render();
        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        layer.Regions = [];
        layer.ThrowOnRender = true;

        Assert.Throws<InvalidOperationException>(() => host.Render());
        layer.ThrowOnRender = false;
        host.DispatchInput(Mouse(9, 9, MouseEventKind.Move));

        AssertRoute(layer, target, UiInputRouteKind.CapturedTarget);
    }

    [Fact]
    public void FailedRender_LeavesCommittedTargetAndFocusForRouting()
    {
        var oldTarget = new UiTargetId("old");
        var newTarget = new UiTargetId("new");
        var layer = Layer(Focus(oldTarget), [new(oldTarget, new Rect(0, 0, 2, 2))]);
        var host = Host(layer);
        host.Render();
        layer.Focus = Focus(newTarget);
        layer.Regions = [new(newTarget, new Rect(5, 5, 2, 2))];
        layer.ThrowOnRender = true;

        Assert.Throws<InvalidOperationException>(() => host.Render());
        host.DispatchInput(Key(ConsoleKey.A));
        AssertRoute(layer, oldTarget, UiInputRouteKind.FocusedTarget);
        host.DispatchInput(Mouse(1, 1, MouseEventKind.Down));
        AssertRoute(layer, oldTarget, UiInputRouteKind.HitTarget);
    }

    [Fact]
    public void RouteContext_EnforcesHostAndInputContracts()
    {
        var layer = Layer();
        var host = Host(layer);
        host.Render();
        Assert.Throws<InvalidOperationException>(() => layer.RouteInput(Key(ConsoleKey.A), UiInputRouteContext.CapturedTarget(layer.FocusScope, new UiTargetId("x"))));
        Assert.Throws<InvalidOperationException>(() => layer.RouteInput(Key(ConsoleKey.A), UiInputRouteContext.Layer(new UiFocusScope())));
        Assert.Throws<InvalidOperationException>(() => layer.RouteInput(Key(ConsoleKey.A), UiInputRouteContext.HitTarget(layer.FocusScope, new UiTargetId("x"))));
    }

    private static TestLayer Layer(UiFocusFrame? focus = null, IReadOnlyList<UiHitRegion>? regions = null, UiLayerInputPolicy policy = UiLayerInputPolicy.Bubble) =>
        new(policy) { Focus = focus ?? UiFocusFrame.Empty, Regions = regions ?? [] };

    private static UiFocusFrame Focus(params UiTargetId[] targets) =>
        new(targets.Select((target, index) => new UiFocusEntry(target, index)).ToArray(), targets.FirstOrDefault());

    private static UiCompositionHost Host(TestLayer layer)
    {
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        host.SetRootSurface(new Surface(host.Screen, layer));
        return host;
    }

    private static void AssertRoute(TestLayer layer, UiTargetId? target, UiInputRouteKind kind)
    {
        Assert.NotNull(layer.LastRoute);
        Assert.Equal(target, layer.LastRoute!.Target);
        Assert.Equal(kind, layer.LastRoute.RouteKind);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) => new(new ConsoleKeyInfo('\0', key, false, false, false));
    private static MouseConsoleInputEvent Mouse(int x, int y, MouseEventKind kind, MouseButton button = MouseButton.Left) => new(x, y, button, kind, MouseKeyModifiers.None);

    private sealed record Frame(UiFocusFrame Focus);

    private sealed class TestLayer(UiLayerInputPolicy policy) : UiLayer<Frame>
    {
        public UiFocusFrame Focus { get; set; } = UiFocusFrame.Empty;
        public IReadOnlyList<UiHitRegion> Regions { get; set; } = [];
        public bool ThrowOnRender { get; set; }
        public int CallCount { get; private set; }
        public UiInputRouteContext? LastRoute { get; private set; }
        public Func<ConsoleInputEvent, UiInputRouteContext, UiInputResult> Result { get; set; } = (_, _) => UiInputResult.NotHandled;
        public override UiLayerInputPolicy InputPolicy => policy;
        protected override Frame RenderFrame(UiRenderContext context) => ThrowOnRender ? throw new InvalidOperationException("render") : new(Focus);
        protected override UiInteractionFrame BuildInteractionFrame(Frame frame) => new(Regions, frame.Focus);
        protected override UiInputResult RouteInput(ConsoleInputEvent input, Frame frame, UiInputRouteContext context)
        {
            CallCount++;
            LastRoute = context;
            return Result(input, context);
        }
    }

    private sealed class Surface(ScreenRenderer screen, TestLayer layer) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => layer.InputPolicy;
        public UiFocusScope FocusScope => layer.FocusScope;
        public UiInteractionFrame CommittedInteractionFrame => layer.CommittedInteractionFrame;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) => layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);
    }
}
