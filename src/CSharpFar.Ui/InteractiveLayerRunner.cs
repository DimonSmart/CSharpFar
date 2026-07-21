namespace CSharpFar.Ui;

internal enum InteractiveLayerPlacement
{
    Overlay,
    TemporarySurface,
}

internal sealed record InteractiveLayerInput<TFrame, TSemantic>(
    UiRoutedInput<TFrame> Routed,
    TSemantic Semantic);

internal readonly record struct InteractiveLayerWakeResult<TResult>(
    bool Invalidate,
    bool IsCompleted,
    TResult Result)
{
    public static InteractiveLayerWakeResult<TResult> NoChange => new(false, false, default!);
    public static InteractiveLayerWakeResult<TResult> Changed => new(true, false, default!);
    public static InteractiveLayerWakeResult<TResult> Complete(TResult result, bool invalidate = false) =>
        new(invalidate, true, result);
}

/// <summary>Owns the synchronous routed-input lifecycle of a composition layer.</summary>
internal sealed class InteractiveLayerRunner
{
    private readonly UiCompositionHost _composition;

    public InteractiveLayerRunner(UiCompositionHost composition) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    public TResult Run<TFrame, TSemantic, TResult>(
        IUiLayer layer,
        InteractiveLayerPlacement placement,
        Func<TFrame> committedFrame,
        TryTakeCompositionPacket<InteractiveLayerInput<TFrame, TSemantic>> tryTakeInput,
        Action<UiFocusRequest> requestFocus,
        Action clearPendingInput,
        Func<UiRoutedInput<TFrame>, TSemantic, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Action<TFrame>? applyCommittedFrame = null,
        Func<DateTimeOffset?>? getNextWakeUtc = null,
        Func<TFrame, InteractiveLayerWakeResult<TResult>>? handleWake = null,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(committedFrame);
        ArgumentNullException.ThrowIfNull(tryTakeInput);
        ArgumentNullException.ThrowIfNull(requestFocus);
        ArgumentNullException.ThrowIfNull(clearPendingInput);
        ArgumentNullException.ThrowIfNull(handleInput);
        if ((getNextWakeUtc is null) != (handleWake is null))
            throw new ArgumentException("Timed interactive layers require both wake scheduling and wake handling.");

        prepareRender?.Invoke();
        IDisposable registration = placement == InteractiveLayerPlacement.Overlay
            ? _composition.PushOverlay(layer)
            : _composition.OpenSurface(new InteractiveSurface(_composition.Screen), layer);
        try
        {
            RenderAndApply(committedFrame, applyCommittedFrame);
            var pump = new CompositionInputPump<InteractiveLayerInput<TFrame, TSemantic>>(
                _composition,
                tryTakeInput,
                static () => { });

            while (true)
            {
                CompositionInputPumpResult<InteractiveLayerInput<TFrame, TSemantic>> read = getNextWakeUtc is null
                    ? CompositionInputPumpResult<InteractiveLayerInput<TFrame, TSemantic>>.Input(pump.Read(cancellationToken))
                    : pump.ReadOrWake(getNextWakeUtc, cancellationToken, wakeSignal);
                if (read.IsWake)
                {
                    InteractiveLayerWakeResult<TResult> wake = handleWake!(committedFrame());
                    if (wake.Invalidate)
                        RenderAndApply(committedFrame, applyCommittedFrame, prepareRender);
                    if (wake.IsCompleted)
                        return wake.Result;
                    continue;
                }

                InteractiveLayerInput<TFrame, TSemantic> packet = read.RequiredPacket;
                ModalDialogLoopResult<TResult> step = handleInput(packet.Routed, packet.Semantic);
                if (step.IsCompleted)
                    return step.Result;

                requestFocus(step.FocusRequest);
                RenderAndApply(committedFrame, applyCommittedFrame, prepareRender);
            }
        }
        finally
        {
            clearPendingInput();
            registration.Dispose();
        }
    }

    private void RenderAndApply<TFrame>(Func<TFrame> committedFrame, Action<TFrame>? applyCommittedFrame, Action? prepareRender = null)
    {
        prepareRender?.Invoke();
        _composition.Render();
        applyCommittedFrame?.Invoke(committedFrame());
    }
}
