using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

/// <summary>Runs a routed, full-screen temporary layer through the shared composition host.</summary>
public sealed class InteractiveSurfaceHost
{
    private readonly UiCompositionHost _composition;

    public InteractiveSurfaceHost(UiCompositionHost composition) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    public TResult Run<TFrame, TSemantic, TResult>(
        InteractiveSurfaceLayer<TFrame, TSemantic> layer,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(handleInput);

        prepareRender?.Invoke();
        using var surface = _composition.OpenSurface(new InteractiveSurface(_composition.Screen, layer));
        _composition.Render();
        applyCommittedFrame?.Invoke(layer.CommittedFrame);

        while (true)
        {
            InteractiveSurfaceInput<TFrame, TSemantic> packet = ReadPacket(layer, cancellationToken);
            ModalDialogLoopResult<TResult> step = handleInput(packet.Routed, packet.Semantic);
            if (step.IsCompleted)
                return step.Result;

            prepareRender?.Invoke();
            _composition.Render();
            applyCommittedFrame?.Invoke(layer.CommittedFrame);
        }
    }

    private InteractiveSurfaceInput<TFrame, TSemantic> ReadPacket<TFrame, TSemantic>(
        InteractiveSurfaceLayer<TFrame, TSemantic> layer,
        CancellationToken cancellationToken)
    {
        if (layer.TryTakeInput(out var pending))
            return pending;

        while (true)
        {
            ConsoleInputEvent input = _composition.ReadInput(cancellationToken);
            _composition.DispatchInput(input);
            if (layer.TryTakeInput(out var packet))
                return packet;
        }
    }
}

internal sealed class InteractiveSurface : IUiSurface, IUiLayer
{
    private readonly ScreenRenderer _screen;
    private readonly IUiLayer _layer;

    public InteractiveSurface(ScreenRenderer screen, IUiLayer layer) => (_screen, _layer) = (screen, layer);
    public UiLayerInputPolicy InputPolicy => _layer.InputPolicy;
    public UiFocusScope FocusScope => _layer.FocusScope;
    public UiInteractionFrame CommittedInteractionFrame => _layer.CommittedInteractionFrame;
    public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
    public void Render(UiRenderContext context) => _layer.Render(context);
    public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => _layer.RouteInput(input, context);
    public void CompleteFrame(UiFrameCompletion completion) { }
}

public class InteractiveSurfaceLayer<TFrame, TSemantic> : UiLayer<TFrame>
{
    private readonly Func<UiRenderContext, UiFocusScope, TFrame> _render;
    private readonly Func<TFrame, UiInteractionFrame> _buildInteractionFrame;
    private readonly Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> _routeInput;
    private InteractiveSurfaceInput<TFrame, TSemantic>? _pendingInput;

    public InteractiveSurfaceLayer(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput) =>
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
        return result.UiResult.Handled
            ? result.UiResult
            : new UiInputResult(true, result.UiResult.Invalidate, result.UiResult.FocusRequest, result.UiResult.MouseCaptureRequest);
    }

    protected virtual (TSemantic Semantic, UiInputResult UiResult) RouteSemanticInput(
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
