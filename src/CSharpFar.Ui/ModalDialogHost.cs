using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public sealed class ModalDialogHost
{
    private readonly UiCompositionHost _composition;

    public ModalDialogHost(UiCompositionHost composition)
    {
        _composition = composition;
    }

    public ScreenRenderer Screen => _composition.Screen;

    public UiCompositionHost Composition => _composition;

    public ModalDialogSession Open(Action<UiRenderContext> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        var layer = new ModalDialogLayer<Unit>(context =>
        {
            render(context);
            return default;
        });
        var overlay = _composition.PushOverlay(layer);
        return new ModalDialogSession(new ModalDialogLayerScope(_composition, overlay), layer);
    }

    public ModalDialogSession<TFrame> Open<TFrame>(Func<UiRenderContext, TFrame> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        var layer = new ModalDialogLayer<TFrame>(render);
        var overlay = _composition.PushOverlay(layer);

        return new ModalDialogSession<TFrame>(new ModalDialogLayerScope(_composition, overlay), layer);
    }

    public TResult Run<TFrame, TResult>(
        Func<UiRenderContext, TFrame> render,
        Func<ConsoleInputEvent, TFrame, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(handleInput);

        prepareRender?.Invoke();
        using var session = Open(render);
        TFrame frame = session.Render();
        applyCommittedFrame?.Invoke(frame);

        while (true)
        {
            ConsoleInputEvent input = session.ReadInput(out frame, cancellationToken);
            applyCommittedFrame?.Invoke(frame);

            ModalDialogLoopResult<TResult> step = handleInput(input, frame);
            if (step.IsCompleted)
                return step.Result;

            prepareRender?.Invoke();
            frame = session.Render();
            applyCommittedFrame?.Invoke(frame);
        }
    }

    public TResult RunRouted<TFrame, TResult>(
        Func<UiRenderContext, TFrame> render,
        Func<UiRoutedInput<TFrame>, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(handleInput);

        prepareRender?.Invoke();
        using var session = Open(render);
        TFrame frame = session.Render();
        applyCommittedFrame?.Invoke(frame);
        while (true)
        {
            UiRoutedInput<TFrame> routed = session.ReadRoutedInput(cancellationToken);
            applyCommittedFrame?.Invoke(routed.Frame);
            ModalDialogLoopResult<TResult> step = handleInput(routed);
            if (step.IsCompleted)
                return step.Result;

            prepareRender?.Invoke();
            frame = session.Render();
            applyCommittedFrame?.Invoke(frame);
        }
    }

    public TResult RunInteractive<TFrame, TSemantic, TResult>(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(buildInteractionFrame);
        ArgumentNullException.ThrowIfNull(routeInput);
        ArgumentNullException.ThrowIfNull(handleInput);

        prepareRender?.Invoke();
        var layer = new InteractiveModalDialogLayer<TFrame, TSemantic>(render, buildInteractionFrame, routeInput);
        using var session = OpenInteractive(layer);
        TFrame frame = session.Render();
        applyCommittedFrame?.Invoke(frame);
        while (true)
        {
            InteractiveModalInput<TFrame, TSemantic> packet = session.ReadInteractiveInput(cancellationToken);
            applyCommittedFrame?.Invoke(packet.Routed.Frame);
            ModalDialogLoopResult<TResult> step = handleInput(packet.Routed, packet.Semantic);
            if (step.IsCompleted)
                return step.Result;

            prepareRender?.Invoke();
            frame = session.Render();
            applyCommittedFrame?.Invoke(frame);
        }
    }

    public void Run<TFrame>(
        Func<UiRenderContext, TFrame> render,
        Func<ConsoleInputEvent, TFrame, ModalDialogLoopAction> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleInput);

        Run(
            render,
            (input, frame) => handleInput(input, frame) == ModalDialogLoopAction.Close
                ? ModalDialogLoopResult<Unit>.Complete(default)
                : ModalDialogLoopResult<Unit>.Continue,
            prepareRender,
            applyCommittedFrame,
            cancellationToken);
    }

    public TResult Run<TResult>(
        Action<UiRenderContext> render,
        Func<ConsoleInputEvent, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(handleInput);

        return Run(
            context =>
            {
                render(context);
                return default(Unit);
            },
            (input, _) => handleInput(input),
            prepareRender,
            applyCommittedFrame: null,
            cancellationToken);
    }

    public void Run(
        Action<UiRenderContext> render,
        Func<ConsoleInputEvent, ModalDialogLoopAction> handleInput,
        Action? prepareRender = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleInput);

        Run(
            render,
            input => handleInput(input) == ModalDialogLoopAction.Close
                ? ModalDialogLoopResult<Unit>.Complete(default)
                : ModalDialogLoopResult<Unit>.Continue,
            prepareRender,
            cancellationToken);
    }

    private InteractiveModalDialogSession<TFrame, TSemantic> OpenInteractive<TFrame, TSemantic>(
        InteractiveModalDialogLayer<TFrame, TSemantic> layer)
    {
        var overlay = _composition.PushOverlay(layer);
        return new InteractiveModalDialogSession<TFrame, TSemantic>(
            new ModalDialogLayerScope(_composition, overlay),
            layer);
    }
}

internal readonly struct Unit;

public sealed class ModalDialogSession : IDisposable
{
    private readonly ModalDialogLayerScope _scope;
    private readonly ModalDialogLayer<Unit> _layer;

    internal ModalDialogSession(ModalDialogLayerScope scope, ModalDialogLayer<Unit> layer) =>
        (_scope, _layer) = (scope, layer);

    public void Render()
    {
        _scope.EnsureActive();
        _scope.Composition.Render();
    }

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default)
    {
        _scope.EnsureActive();
        if (_layer.TryTakeInput(out var pending))
            return pending.Input;

        while (true)
        {
            ConsoleInputEvent semanticInput = _scope.Composition.ReadCompositionInput(cancellationToken);
            _scope.Composition.DispatchInput(semanticInput);

            if (_layer.TryTakeInput(out var routed))
                return routed.Input;
        }
    }

    public bool TryReadInput(out ConsoleInputEvent? input)
    {
        _scope.EnsureActive();
        if (_layer.TryTakeInput(out var pending))
        {
            input = pending.Input;
            return true;
        }

        while (_scope.Composition.TryReadCompositionInput(out ConsoleInputEvent? semanticInput))
        {
            if (semanticInput is null)
                continue;

            UiInputResult dispatch = _scope.Composition.DispatchInput(semanticInput);
            if (dispatch.Invalidate)
                _scope.Composition.Render();

            if (_layer.TryTakeInput(out var routed))
            {
                input = routed.Input;
                return true;
            }
        }

        input = null;
        return false;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _layer.ClearPendingInput();
    }
}

public sealed class ModalDialogSession<TFrame> : IDisposable
{
    private readonly ModalDialogLayerScope _scope;
    private readonly ModalDialogLayer<TFrame> _layer;

    internal ModalDialogSession(ModalDialogLayerScope scope, ModalDialogLayer<TFrame> layer) =>
        (_scope, _layer) = (scope, layer);

    public TFrame Render()
    {
        _scope.EnsureActive();
        _scope.Composition.Render();
        return _layer.CommittedFrame;
    }

    public ConsoleInputEvent ReadInput(out TFrame frame, CancellationToken cancellationToken = default)
    {
        UiRoutedInput<TFrame> routed = ReadRoutedInput(cancellationToken);
        frame = routed.Frame;
        return routed.Input;
    }

    public UiRoutedInput<TFrame> ReadRoutedInput(CancellationToken cancellationToken = default)
    {
        _scope.EnsureActive();
        if (_layer.TryTakeInput(out var pending))
            return pending;

        while (true)
        {
            ConsoleInputEvent semanticInput = _scope.Composition.ReadCompositionInput(cancellationToken);
            UiInputResult dispatch = _scope.Composition.DispatchInput(semanticInput);
            if (dispatch.Invalidate)
                _scope.Composition.Render();

            if (!_layer.TryTakeInput(out var routed))
                continue;

            return routed;
        }
    }

    public bool TryReadInput(out ConsoleInputEvent? input, out TFrame frame)
    {
        if (TryReadRoutedInput(out var routed))
        {
            input = routed.Input;
            frame = routed.Frame;
            return true;
        }

        input = null;
        frame = _layer.CommittedFrame;
        return false;
    }

    public bool TryReadRoutedInput(out UiRoutedInput<TFrame> routed)
    {
        _scope.EnsureActive();
        if (_layer.TryTakeInput(out var pending))
        {
            routed = pending;
            return true;
        }

        while (_scope.Composition.TryReadCompositionInput(out ConsoleInputEvent? semanticInput))
        {
            if (semanticInput is null)
                continue;

            UiInputResult dispatch = _scope.Composition.DispatchInput(semanticInput);
            if (dispatch.Invalidate)
                _scope.Composition.Render();

            if (!_layer.TryTakeInput(out var pendingRouted))
                continue;

            routed = pendingRouted;
            return true;
        }

        routed = null!;
        return false;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _layer.ClearPendingInput();
    }
}

internal sealed class InteractiveModalDialogSession<TFrame, TSemantic> : IDisposable
{
    private readonly ModalDialogLayerScope _scope;
    private readonly InteractiveModalDialogLayer<TFrame, TSemantic> _layer;

    internal InteractiveModalDialogSession(
        ModalDialogLayerScope scope,
        InteractiveModalDialogLayer<TFrame, TSemantic> layer) =>
        (_scope, _layer) = (scope, layer);

    public TFrame Render()
    {
        _scope.EnsureActive();
        _scope.Composition.Render();
        return _layer.CommittedFrame;
    }

    public InteractiveModalInput<TFrame, TSemantic> ReadInteractiveInput(CancellationToken cancellationToken = default)
    {
        _scope.EnsureActive();
        if (_layer.TryTakeInput(out var pending))
            return pending;

        while (true)
        {
            ConsoleInputEvent semanticInput = _scope.Composition.ReadCompositionInput(cancellationToken);
            UiInputResult dispatch = _scope.Composition.DispatchInput(semanticInput);
            if (dispatch.Invalidate)
                _scope.Composition.Render();

            if (_layer.TryTakeInput(out var routed))
                return routed;
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
        _layer.ClearPendingInput();
    }
}

internal sealed record InteractiveModalInput<TFrame, TSemantic>(
    UiRoutedInput<TFrame> Routed,
    TSemantic Semantic,
    bool Invalidate);

internal sealed class ModalDialogLayerScope : IDisposable
{
    private UiCompositionHost? _composition;
    private IDisposable? _overlay;

    internal ModalDialogLayerScope(UiCompositionHost composition, IDisposable overlay) =>
        (_composition, _overlay) = (composition, overlay);

    public bool IsDisposed => _composition is null || _overlay is null;

    public UiCompositionHost Composition => _composition ?? throw new ObjectDisposedException(nameof(ModalDialogSession));

    public void EnsureActive()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(ModalDialogSession));
    }

    public void Dispose()
    {
        var composition = _composition;
        var overlay = _overlay;
        if (composition is null || overlay is null)
            return;

        overlay.Dispose();
        _overlay = null;
        _composition = null;
        composition.Render();
    }
}

internal sealed class ModalDialogLayer<TFrame> : UiLayer<TFrame>
{
    private readonly Func<UiRenderContext, TFrame> _render;
    private UiRoutedInput<TFrame>? _pendingInput;

    public ModalDialogLayer(Func<UiRenderContext, TFrame> render) =>
        _render = render;

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Modal;

    protected override TFrame RenderFrame(UiRenderContext context) =>
        _render(context);

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TFrame frame,
        UiInputRouteContext context)
    {
        if (_pendingInput is not null)
            throw new InvalidOperationException("Modal input was dispatched before the previous input was consumed.");

        _pendingInput = new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind);
        return UiInputResult.HandledResult;
    }

    public bool TryTakeInput(out UiRoutedInput<TFrame> routed)
    {
        if (_pendingInput is null)
        {
            routed = null!;
            return false;
        }

        routed = _pendingInput;
        _pendingInput = null;
        return true;
    }

    internal void ClearPendingInput() => _pendingInput = null;
}

internal sealed class InteractiveModalDialogLayer<TFrame, TSemantic> : UiLayer<TFrame>
{
    private readonly Func<UiRenderContext, UiFocusScope, TFrame> _render;
    private readonly Func<TFrame, UiInteractionFrame> _buildInteractionFrame;
    private readonly Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> _routeInput;
    private InteractiveModalInput<TFrame, TSemantic>? _pendingInput;

    public InteractiveModalDialogLayer(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput) =>
        (_render, _buildInteractionFrame, _routeInput) = (render, buildInteractionFrame, routeInput);

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Modal;

    protected override TFrame RenderFrame(UiRenderContext context) =>
        _render(context, FocusScope);

    protected override UiInteractionFrame BuildInteractionFrame(TFrame frame) =>
        _buildInteractionFrame(frame);

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TFrame frame,
        UiInputRouteContext context)
    {
        if (_pendingInput is not null)
            throw new InvalidOperationException("Modal input was dispatched before the previous input was consumed.");

        var routed = _routeInput(input, frame, context);
        _pendingInput = new InteractiveModalInput<TFrame, TSemantic>(
            new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind),
            routed.Semantic,
            routed.UiResult.Invalidate);
        UiInputResult result = routed.UiResult;
        return result.Handled
            ? result
            : new UiInputResult(true, result.Invalidate, result.FocusRequest, result.MouseCaptureRequest);
    }

    public bool TryTakeInput(out InteractiveModalInput<TFrame, TSemantic> routed)
    {
        if (_pendingInput is null)
        {
            routed = null!;
            return false;
        }

        routed = _pendingInput;
        _pendingInput = null;
        return true;
    }

    internal void ClearPendingInput() => _pendingInput = null;
}
