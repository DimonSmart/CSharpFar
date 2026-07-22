using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

/// <summary>Runs a routed, full-screen temporary layer through the shared composition host.</summary>
public sealed class InteractiveSurfaceHost
{
    private readonly UiCompositionHost _composition;

    public InteractiveSurfaceHost(UiCompositionHost composition) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    /// <summary>
    /// Runs an interactive temporary surface until the domain handler completes it.
    /// Frame-dependent surface state is synchronized only through
    /// <see cref="UiLayer{TFrame}.OnFrameCommitted(TFrame)"/>. The runner does
    /// not provide a second committed-frame callback: rejected render attempts
    /// do not publish committed state, and automatic resize recovery uses the
    /// same stable-frame commit lifecycle as ordinary renders.
    /// </summary>
    public TResult Run<TFrame, TSemantic, TResult>(
        InteractiveSurfaceLayer<TFrame, TSemantic> layer,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Func<DateTimeOffset?>? getNextWakeUtc = null,
        Func<TFrame, InteractiveSurfaceWakeResult>? handleWake = null,
        CancellationToken cancellationToken = default)
    {
        return new InteractiveLayerRunner(_composition).Run(
            layer,
            InteractiveLayerPlacement.TemporarySurface,
            () => layer.CommittedFrame,
            layer.TryTakeInteractiveInput,
            layer.RequestFocusOnNextCommit,
            layer.ClearPendingInput,
            handleInput,
            prepareRender,
            applyCommittedFrame: null,
            getNextWakeUtc,
            handleWake is null ? null : frame =>
                new InteractiveLayerWakeResult<TResult>(handleWake(frame).Invalidate, false, default!),
            cancellationToken);
    }

    /// <summary>Runs a timed temporary surface whose wake handler may complete the session.</summary>
    public TResult RunTimed<TFrame, TSemantic, TResult>(
        InteractiveSurfaceLayer<TFrame, TSemantic> layer,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Func<DateTimeOffset?> getNextWakeUtc,
        Func<TFrame, InteractiveSurfaceWakeResult<TResult>> handleWake,
        Action? prepareRender = null,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(getNextWakeUtc);
        ArgumentNullException.ThrowIfNull(handleWake);

        return new InteractiveLayerRunner(_composition).Run(
            layer,
            InteractiveLayerPlacement.TemporarySurface,
            () => layer.CommittedFrame,
            layer.TryTakeInteractiveInput,
            layer.RequestFocusOnNextCommit,
            layer.ClearPendingInput,
            handleInput,
            prepareRender,
            applyCommittedFrame: null,
            getNextWakeUtc,
            frame =>
            {
                InteractiveSurfaceWakeResult<TResult> wake = handleWake(frame);
                return new InteractiveLayerWakeResult<TResult>(wake.Invalidate, wake.IsCompleted, wake.Result);
            },
            cancellationToken,
            wakeSignal);
    }
}

public readonly record struct InteractiveSurfaceWakeResult(bool Invalidate)
{
    public static InteractiveSurfaceWakeResult NoChange => new(Invalidate: false);

    public static InteractiveSurfaceWakeResult Changed => new(Invalidate: true);
}

internal sealed class InteractiveSurface : IUiSurface
{
    private readonly ScreenRenderer _screen;

    public InteractiveSurface(ScreenRenderer screen) => _screen = screen;
    public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
    public void Render(UiRenderContext context) { }
    public void CompleteFrame(UiFrameCompletion completion) { }
}

public readonly record struct InteractiveSurfaceRouteResult<TSemantic>(
    TSemantic Semantic,
    bool Invalidate = false,
    UiFocusRequest FocusRequest = default,
    UiMouseCaptureRequest MouseCaptureRequest = default)
{
    internal UiInputResult ToUiInputResult() =>
        new(true, Invalidate, FocusRequest, MouseCaptureRequest);
}

public class InteractiveSurfaceLayer<TFrame, TSemantic> : UiLayer<TFrame>
{
    private readonly Func<UiRenderContext, IUiFocusState, TFrame> _render;
    private readonly Func<TFrame, UiInteractionFrame> _buildInteractionFrame;
    private readonly Func<ConsoleInputEvent, TFrame, UiInputRouteContext, InteractiveSurfaceRouteResult<TSemantic>> _routeInput;
    private readonly PendingInputSlot<InteractiveLayerInput<TFrame, TSemantic>> _pendingInput = new();

    public InteractiveSurfaceLayer(
        Func<UiRenderContext, IUiFocusState, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, InteractiveSurfaceRouteResult<TSemantic>> routeInput) =>
        (_render, _buildInteractionFrame, _routeInput) = (render, buildInteractionFrame, routeInput);

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Modal;

    protected virtual TFrame RenderFrameCore(UiRenderContext context) => _render(context, FocusState);

    protected override TFrame RenderFrame(UiRenderContext context) => RenderFrameCore(context);

    protected virtual UiInteractionFrame BuildInteractionFrameCore(TFrame frame) => _buildInteractionFrame(frame);

    protected override UiInteractionFrame BuildInteractionFrame(TFrame frame) => BuildInteractionFrameCore(frame);

    protected override UiInputResult RouteInput(ConsoleInputEvent input, TFrame frame, UiInputRouteContext context)
    {
        var result = RouteSemanticInput(input, frame, context);
        _pendingInput.Store(new InteractiveLayerInput<TFrame, TSemantic>(
            new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind), result.Semantic));
        return result.ToUiInputResult();
    }

    protected virtual InteractiveSurfaceRouteResult<TSemantic> RouteSemanticInput(
        ConsoleInputEvent input,
        TFrame frame,
        UiInputRouteContext context) => _routeInput(input, frame, context);

    public bool TryTakeInput(out InteractiveSurfaceInput<TFrame, TSemantic> input)
    {
        if (!TryTakeInteractiveInput(out var packet))
        {
            input = null!;
            return false;
        }

        input = new InteractiveSurfaceInput<TFrame, TSemantic>(packet.Routed, packet.Semantic);
        return true;
    }

    internal bool TryTakeInteractiveInput(out InteractiveLayerInput<TFrame, TSemantic> input) =>
        _pendingInput.TryTake(out input);

    internal void ClearPendingInput() => _pendingInput.Clear();
}

public readonly record struct InteractiveSurfaceWakeResult<TResult>(
    bool Invalidate,
    bool IsCompleted,
    TResult Result)
{
    public static InteractiveSurfaceWakeResult<TResult> NoChange => new(false, false, default!);

    public static InteractiveSurfaceWakeResult<TResult> Changed => new(true, false, default!);

    public static InteractiveSurfaceWakeResult<TResult> Complete(TResult result, bool invalidate = false) =>
        new(invalidate, true, result);
}

public sealed record InteractiveSurfaceInput<TFrame, TSemantic>(UiRoutedInput<TFrame> Routed, TSemantic Semantic);
