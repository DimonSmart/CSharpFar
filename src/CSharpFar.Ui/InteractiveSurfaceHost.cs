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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(handleInput);

        prepareRender?.Invoke();
        using var surface = _composition.OpenSurface(new InteractiveSurface(_composition.Screen), layer);
        _composition.Render();

        while (true)
        {
            InteractiveSurfaceInput<TFrame, TSemantic> packet =
                new CompositionInputPump<InteractiveSurfaceInput<TFrame, TSemantic>>(
                    _composition,
                    layer.TryTakeInput)
                .Read(cancellationToken);
            ModalDialogLoopResult<TResult> step = handleInput(packet.Routed, packet.Semantic);
            if (step.IsCompleted)
                return step.Result;

            prepareRender?.Invoke();
            _composition.Render();
        }
    }
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
    private readonly Func<UiRenderContext, UiFocusScope, TFrame> _render;
    private readonly Func<TFrame, UiInteractionFrame> _buildInteractionFrame;
    private readonly Func<ConsoleInputEvent, TFrame, UiInputRouteContext, InteractiveSurfaceRouteResult<TSemantic>> _routeInput;
    private InteractiveSurfaceInput<TFrame, TSemantic>? _pendingInput;

    public InteractiveSurfaceLayer(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, InteractiveSurfaceRouteResult<TSemantic>> routeInput) =>
        (_render, _buildInteractionFrame, _routeInput) = (render, buildInteractionFrame, routeInput);

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Modal;

    protected virtual TFrame RenderFrameCore(UiRenderContext context) => _render(context, FocusScope);

    protected override TFrame RenderFrame(UiRenderContext context) => RenderFrameCore(context);

    protected virtual UiInteractionFrame BuildInteractionFrameCore(TFrame frame) => _buildInteractionFrame(frame);

    protected override UiInteractionFrame BuildInteractionFrame(TFrame frame) => BuildInteractionFrameCore(frame);

    protected override UiInputResult RouteInput(ConsoleInputEvent input, TFrame frame, UiInputRouteContext context)
    {
        if (_pendingInput is not null)
            throw new InvalidOperationException("Surface input was dispatched before the previous input was consumed.");

        var result = RouteSemanticInput(input, frame, context);
        _pendingInput = new InteractiveSurfaceInput<TFrame, TSemantic>(
            new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind), result.Semantic);
        return result.ToUiInputResult();
    }

    protected virtual InteractiveSurfaceRouteResult<TSemantic> RouteSemanticInput(
        ConsoleInputEvent input,
        TFrame frame,
        UiInputRouteContext context) => _routeInput(input, frame, context);

    public bool TryTakeInput(out InteractiveSurfaceInput<TFrame, TSemantic> input)
    {
        if (_pendingInput is null)
        {
            input = null!;
            return false;
        }

        input = _pendingInput;
        _pendingInput = null;
        return true;
    }
}

public sealed record InteractiveSurfaceInput<TFrame, TSemantic>(UiRoutedInput<TFrame> Routed, TSemantic Semantic);
