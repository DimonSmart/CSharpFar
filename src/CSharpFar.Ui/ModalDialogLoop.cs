namespace CSharpFar.Ui;

public readonly struct ModalDialogLoopResult<TResult>
{
    private readonly TResult? _result;

    private ModalDialogLoopResult(bool isCompleted, TResult? result, bool invalidate, UiFocusRequest focusRequest)
    {
        IsCompleted = isCompleted;
        _result = result;
        Invalidate = invalidate;
        FocusRequest = focusRequest;
    }

    public bool IsCompleted { get; }

    public UiFocusRequest FocusRequest { get; }

    public bool Invalidate { get; }

    public TResult Result => IsCompleted
        ? _result!
        : throw new InvalidOperationException("A continuing modal loop has no result.");

    /// <summary>
    /// Continues when the semantic handler introduced no render-visible change that is not already represented by routed invalidation.
    /// </summary>
    public static ModalDialogLoopResult<TResult> ContinueNoChange => new(false, default, false, UiFocusRequest.None);

    /// <summary>
    /// Continues and commits a render because the semantic handler introduced render-visible state.
    /// </summary>
    public static ModalDialogLoopResult<TResult> ContinueChanged => new(false, default, true, UiFocusRequest.None);

    public static ModalDialogLoopResult<TResult> ContinueWithFocus(UiTargetId target) =>
        new(false, default, true, UiFocusRequest.Set(target));

    public static ModalDialogLoopResult<TResult> Complete(TResult result) =>
        new(true, result, false, UiFocusRequest.None);
}

public enum ModalDialogLoopAction
{
    Continue,
    Close,
}

public readonly struct ModalDialogWakeResult<TResult>
{
    private readonly ModalDialogLoopResult<TResult> _loopResult;

    private ModalDialogWakeResult(bool invalidate, ModalDialogLoopResult<TResult> loopResult)
    {
        Invalidate = invalidate;
        _loopResult = loopResult;
    }

    public bool Invalidate { get; }

    public bool IsCompleted => _loopResult.IsCompleted;

    public TResult Result => _loopResult.Result;

    public static ModalDialogWakeResult<TResult> NoChange { get; } =
        new(false, ModalDialogLoopResult<TResult>.ContinueNoChange);

    public static ModalDialogWakeResult<TResult> Changed { get; } =
        new(true, ModalDialogLoopResult<TResult>.ContinueNoChange);

    public static ModalDialogWakeResult<TResult> Complete(TResult result) =>
        new(false, ModalDialogLoopResult<TResult>.Complete(result));

    public static ModalDialogWakeResult<TResult> Complete(TResult result, bool invalidate) =>
        new(invalidate, ModalDialogLoopResult<TResult>.Complete(result));
}
