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
        var calls = new List<string>();
        var surface = new CaptureLayer("surface", calls);
        var fixture = new UiLayerTestHost(surface);
        var host = fixture.Composition;
        var owner = new CaptureLayer("owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top", calls);
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        fixture.Dispatch(UiTestInput.Mouse(1, 1, MouseEventKind.Down));
        owner.Result = UiInputResult.NotHandled;
        top.Result = UiInputResult.HandledResult;
        fixture.Dispatch(UiTestInput.Mouse(1, 1, MouseEventKind.Move));

        Assert.Contains(owner.Contexts, context => context.IsCapturedRoute && context.Target == new UiTargetId("thumb"));
        Assert.Equal(2, owner.Calls.Count);
        Assert.Single(top.Calls);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void ExplicitRelease_ClearsCapture()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var owner = new CaptureLayer("owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top", calls);
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));
        owner.Result = UiInputResult.ReleaseMouse();
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));
        calls.Clear();
        owner.Result = UiInputResult.NotHandled;
        top.Result = UiInputResult.HandledResult;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["top"], calls);
    }

    [Fact]
    public void NewCapture_ReplacesOldCapture()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var first = new CaptureLayer("first", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("first"), MouseButton.Left),
        };
        var second = new CaptureLayer("second", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("second"), MouseButton.Right),
        };
        using var firstScope = host.PushOverlay(first);
        using var secondScope = host.PushOverlay(second);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Right));
        calls.Clear();
        first.Result = UiInputResult.NotHandled;
        second.Result = UiInputResult.NotHandled;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Right));

        Assert.Equal(["second"], calls);
        Assert.Contains(second.Contexts, context => context is { IsCapturedRoute: true, Target.Value: "second" });
    }

    [Fact]
    public void MatchingUp_ReleasesCapture()
    {
        var (host, _) = Fixture();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        owner.Result = UiInputResult.NotHandled;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Up, MouseButton.Left));
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(2, owner.Calls.Count);
        Assert.Equal(2, top.Calls.Count);
    }

    [Fact]
    public void NonMatchingUp_DoesNotReleaseCapture()
    {
        var (host, _) = Fixture();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        owner.Result = UiInputResult.NotHandled;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Up, MouseButton.Right));
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(3, owner.Calls.Count);
        Assert.Single(top.Calls);
    }

    [Fact]
    public void DisposeOwner_ClearsCapture()
    {
        var (host, _) = Fixture();
        var owner = new CaptureLayer("owner")
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top");
        var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));
        top.Result = UiInputResult.HandledResult;
        topScope.Dispose();
        ownerScope.Dispose();
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Single(owner.Calls);
    }

    [Fact]
    public void InvalidCaptureRequestsThrow()
    {
        var (host, surface) = Fixture();
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left);

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(UiTestInput.Key(ConsoleKey.A)));
        surface.Result = new UiInputResult(
            false,
            false,
            UiFocusRequest.None,
            UiMouseCaptureRequest.Capture(new UiTargetId("thumb"), MouseButton.Left));
        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left)));
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Right);
        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left)));
        Assert.Throws<ArgumentException>(() => UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.WheelUp));
    }

    [Fact]
    public void CaptureRequest_ForUnknownCommittedTargetThrows()
    {
        var (host, surface) = Fixture();
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("missing"), MouseButton.Left);

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left)));
    }

    [Fact]
    public void NonePolicyLayer_IsSkippedAndCannotEstablishCapture()
    {
        var (host, surface) = Fixture();
        var none = new CaptureLayer("none", policy: UiLayerInputPolicy.None)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        using var scope = host.PushOverlay(none);

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Empty(none.Calls);
        Assert.Equal(2, surface.Calls.Count);
        Assert.DoesNotContain(surface.Contexts, context => context.IsCapturedRoute);
    }

    [Fact]
    public void LayerChangingToNonePolicyBeforeCaptureValidationIsRejected()
    {
        var (host, surface) = Fixture();
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left);
        surface.OnRoute = () => surface.Policy = UiLayerInputPolicy.None;

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left)));
    }

    [Fact]
    public void ModalOverlayAboveOwner_PreemptsCaptureAndBlocksCapturedOwner()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var owner = new CaptureLayer("owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        using var ownerScope = host.PushOverlay(owner);
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));

        var modal = new CaptureLayer("modal", calls, UiLayerInputPolicy.Modal);
        using var modalScope = host.PushOverlay(modal);
        calls.Clear();
        owner.Result = UiInputResult.NotHandled;

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["modal"], calls);
    }

    [Fact]
    public void TemporarySurfaceClearsRootCaptureAndDoesNotRestoreIt()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("root"), MouseButton.Left);
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));

        var temporary = new CaptureLayer("temporary", calls);
        using (host.OpenSurface(new UiLayerTestSurface(host.Screen, temporary)))
        {
            calls.Clear();
            surface.Result = UiInputResult.NotHandled;
            temporary.Result = UiInputResult.HandledResult;
            host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));
            Assert.Equal(["temporary"], calls);
        }

        calls.Clear();
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["surface"], calls);
        Assert.DoesNotContain(surface.Contexts, context => context.IsCapturedRoute);
    }

    [Fact]
    public void OwnerWithNonePolicy_LosesCaptureAndCurrentEventUsesNormalRouting()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var owner = new CaptureLayer("owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        var top = new CaptureLayer("top", calls);
        using var ownerScope = host.PushOverlay(owner);
        using var topScope = host.PushOverlay(top);
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));

        calls.Clear();
        owner.Policy = UiLayerInputPolicy.None;
        top.Result = UiInputResult.HandledResult;
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["top"], calls);
    }

    [Fact]
    public void OwnerAbsentFromActiveComposition_LosesCaptureAndCurrentEventUsesNormalRouting()
    {
        var calls = new List<string>();
        var (host, _) = Fixture(calls);
        var owner = new CaptureLayer("owner", calls)
        {
            Result = UiInputResult.CaptureMouse(new UiTargetId("thumb"), MouseButton.Left),
        };
        using var ownerScope = host.PushOverlay(owner);
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));

        var temporary = new CaptureLayer("temporary", calls)
        {
            Result = UiInputResult.HandledResult,
        };
        using var temporaryScope = host.OpenSurface(new UiLayerTestSurface(host.Screen, temporary));
        calls.Clear();
        owner.Result = UiInputResult.NotHandled;

        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["temporary"], calls);
    }

    [Fact]
    public void ReplaceRoot_ClearsCapture()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        surface.Result = UiInputResult.CaptureMouse(new UiTargetId("root"), MouseButton.Left);
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Down, MouseButton.Left));

        var replacement = new CaptureLayer("replacement", calls)
        {
            Result = UiInputResult.HandledResult,
        };
        host.SetRootSurface(new UiLayerTestSurface(host.Screen, replacement));
        calls.Clear();
        host.DispatchInput(UiTestInput.Mouse(1, 1, MouseEventKind.Move, MouseButton.Left));

        Assert.Equal(["replacement"], calls);
    }

    private static (UiCompositionHost Host, CaptureLayer Surface) Fixture(List<string>? calls = null)
    {
        var surface = new CaptureLayer("surface", calls);
        var fixture = new UiLayerTestHost(surface);
        return (fixture.Composition, surface);
    }

    private sealed class CaptureLayer(
        string name,
        List<string>? sharedCalls = null,
        UiLayerInputPolicy policy = UiLayerInputPolicy.Bubble) : IUiLayer
    {
        public UiLayerInputPolicy Policy { get; set; } = policy;
        public UiLayerInputPolicy InputPolicy => Policy;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame { get; } = new([
            new(new UiTargetId("thumb"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
            new(new UiTargetId("first"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
            new(new UiTargetId("second"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
            new(new UiTargetId("root"), new CSharpFar.Console.Models.Rect(0, 0, 1, 1)),
        ]);
        public List<string> Calls { get; } = [];
        public List<UiInputRouteContext> Contexts { get; } = [];
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public Action? OnRoute { get; set; }
        public void Render(UiRenderContext context) { }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            Calls.Add(name);
            sharedCalls?.Add(name);
            Contexts.Add(context);
            OnRoute?.Invoke();
            return Result;
        }
    }

}
