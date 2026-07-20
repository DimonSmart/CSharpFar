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
        ArgumentNullException.ThrowIfNull(handleInput);

        return RunRouted(
            render,
            routed => handleInput(routed.Input, routed.Frame),
            prepareRender,
            applyCommittedFrame,
            cancellationToken);
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

        return RunInteractiveCore<TFrame, Unit, TResult>(
            (context, _) => render(context),
            static _ => UiInteractionFrame.Empty,
            static (_, _, _) => (default, UiInputResult.HandledResult),
            (routed, _) => handleInput(routed),
            prepareRender,
            applyCommittedFrame,
            true,
            getNextWakeUtc: null,
            handleWake: null,
            cancellationToken);
    }

    /// <summary>
    /// Runs an interactive modal dialog. <paramref name="routeInput"/> receives the
    /// committed frame and restores any frame-dependent component state before it
    /// changes that state. Those changes are pending for the semantic handler;
    /// <paramref name="applyCommittedFrame"/> is called only for frames accepted by
    /// a successful render, never between routing and semantic handling or for a
    /// rejected render attempt.
    /// </summary>
    public TResult RunInteractive<TFrame, TSemantic, TResult>(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default)
    {
        return RunInteractiveCore(
            render,
            buildInteractionFrame,
            routeInput,
            handleInput,
            prepareRender,
            applyCommittedFrame,
            false,
            getNextWakeUtc: null,
            handleWake: null,
            cancellationToken);
    }

    public TResult RunInteractiveTimed<TFrame, TSemantic, TResult>(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Func<DateTimeOffset?> getNextWakeUtc,
        Func<TFrame, ModalDialogWakeResult<TResult>> handleWake,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(getNextWakeUtc);
        ArgumentNullException.ThrowIfNull(handleWake);

        return RunInteractiveCore(
            render,
            buildInteractionFrame,
            routeInput,
            handleInput,
            prepareRender,
            applyCommittedFrame,
            false,
            getNextWakeUtc,
            handleWake,
            cancellationToken,
            wakeSignal);
    }

    private TResult RunInteractiveCore<TFrame, TSemantic, TResult>(
        Func<UiRenderContext, UiFocusScope, TFrame> render,
        Func<TFrame, UiInteractionFrame> buildInteractionFrame,
        Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> routeInput,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender,
        Action<TFrame>? applyCommittedFrame,
        bool applyCommittedFrameBeforeHandler,
        Func<DateTimeOffset?>? getNextWakeUtc,
        Func<TFrame, ModalDialogWakeResult<TResult>>? handleWake,
        CancellationToken cancellationToken,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(buildInteractionFrame);
        ArgumentNullException.ThrowIfNull(routeInput);
        ArgumentNullException.ThrowIfNull(handleInput);
        if ((getNextWakeUtc is null) != (handleWake is null))
            throw new ArgumentException("Timed interactive modals require both wake scheduling and wake handling.");

        prepareRender?.Invoke();
        var layer = new InteractiveModalDialogLayer<TFrame, TSemantic>(render, buildInteractionFrame, routeInput);
        using var session = OpenInteractive(layer);
        TFrame frame = session.Render();
        applyCommittedFrame?.Invoke(frame);
        while (true)
        {
            CompositionInputPumpResult<InteractiveModalInput<TFrame, TSemantic>> read = session.ReadInteractiveInputOrWake(
                getNextWakeUtc,
                cancellationToken,
                wakeSignal);
            if (read.IsWake)
            {
                ModalDialogWakeResult<TResult> wake = handleWake!(layer.CommittedFrame);
                if (wake.Invalidate)
                {
                    prepareRender?.Invoke();
                    frame = session.Render();
                    applyCommittedFrame?.Invoke(frame);
                }

                if (wake.IsCompleted)
                    return wake.Result;

                continue;
            }

            InteractiveModalInput<TFrame, TSemantic> packet = read.RequiredPacket;
            if (applyCommittedFrameBeforeHandler)
                applyCommittedFrame?.Invoke(packet.Routed.Frame);
            ModalDialogLoopResult<TResult> step = handleInput(packet.Routed, packet.Semantic);
            if (step.IsCompleted)
                return step.Result;

            layer.RequestFocusOnNextCommit(step.FocusRequest);

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
    private readonly CompositionInputPump<UiRoutedInput<Unit>> _pump;

    internal ModalDialogSession(ModalDialogLayerScope scope, ModalDialogLayer<Unit> layer)
    {
        (_scope, _layer) = (scope, layer);
        _pump = new CompositionInputPump<UiRoutedInput<Unit>>(
            scope.Composition,
            layer.TryTakeInput,
            scope.EnsureActive);
    }

    public void Render()
    {
        _scope.EnsureActive();
        _scope.Composition.Render();
    }

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default)
    {
        _scope.EnsureActive();
        return _pump.Read(cancellationToken).Input;
    }

    public bool TryReadInput(out ConsoleInputEvent? input)
    {
        bool hasPacket = _pump.TryRead(out var routed);
        input = hasPacket ? routed.Input : null;
        return hasPacket;
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
    private readonly CompositionInputPump<UiRoutedInput<TFrame>> _pump;

    internal ModalDialogSession(ModalDialogLayerScope scope, ModalDialogLayer<TFrame> layer)
    {
        (_scope, _layer) = (scope, layer);
        _pump = new CompositionInputPump<UiRoutedInput<TFrame>>(
            scope.Composition,
            layer.TryTakeInput,
            scope.EnsureActive);
    }

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
        return _pump.Read(cancellationToken);
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
        return _pump.TryRead(out routed);
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
    private readonly CompositionInputPump<InteractiveModalInput<TFrame, TSemantic>> _pump;

    internal InteractiveModalDialogSession(
        ModalDialogLayerScope scope,
        InteractiveModalDialogLayer<TFrame, TSemantic> layer)
    {
        (_scope, _layer) = (scope, layer);
        _pump = new CompositionInputPump<InteractiveModalInput<TFrame, TSemantic>>(
            scope.Composition,
            layer.TryTakeInput,
            scope.EnsureActive);
    }

    public TFrame Render()
    {
        _scope.EnsureActive();
        _scope.Composition.Render();
        return _layer.CommittedFrame;
    }

    public InteractiveModalInput<TFrame, TSemantic> ReadInteractiveInput(CancellationToken cancellationToken = default)
    {
        return _pump.Read(cancellationToken);
    }

    public CompositionInputPumpResult<InteractiveModalInput<TFrame, TSemantic>> ReadInteractiveInputOrWake(
        Func<DateTimeOffset?>? getNextWakeUtc,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        return getNextWakeUtc is null
            ? CompositionInputPumpResult<InteractiveModalInput<TFrame, TSemantic>>.Input(_pump.Read(cancellationToken))
            : _pump.ReadOrWake(getNextWakeUtc, cancellationToken, wakeSignal);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _layer.ClearPendingInput();
    }
}

internal sealed record InteractiveModalInput<TFrame, TSemantic>(
    UiRoutedInput<TFrame> Routed,
    TSemantic Semantic);

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
    private readonly PendingInputSlot<UiRoutedInput<TFrame>> _pendingInput = new();

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
        _pendingInput.Store(new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind));
        return UiInputResult.HandledResult;
    }

    public bool TryTakeInput(out UiRoutedInput<TFrame> routed)
    {
        return _pendingInput.TryTake(out routed);
    }

    internal void ClearPendingInput() => _pendingInput.Clear();
}

internal sealed class InteractiveModalDialogLayer<TFrame, TSemantic> : UiLayer<TFrame>
{
    private readonly Func<UiRenderContext, UiFocusScope, TFrame> _render;
    private readonly Func<TFrame, UiInteractionFrame> _buildInteractionFrame;
    private readonly Func<ConsoleInputEvent, TFrame, UiInputRouteContext, (TSemantic Semantic, UiInputResult UiResult)> _routeInput;
    private readonly PendingInputSlot<InteractiveModalInput<TFrame, TSemantic>> _pendingInput = new();

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
        var routed = _routeInput(input, frame, context);
        _pendingInput.Store(new InteractiveModalInput<TFrame, TSemantic>(
            new UiRoutedInput<TFrame>(input, frame, context.Target, context.RouteKind),
            routed.Semantic));
        UiInputResult result = routed.UiResult;
        return result.Handled
            ? result
            : new UiInputResult(true, result.Invalidate, result.FocusRequest, result.MouseCaptureRequest);
    }

    public bool TryTakeInput(out InteractiveModalInput<TFrame, TSemantic> routed)
    {
        return _pendingInput.TryTake(out routed);
    }

    internal void ClearPendingInput() => _pendingInput.Clear();
}
