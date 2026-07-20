namespace CSharpFar.Ui;

public readonly struct ModalDialogLoopResult<TResult>
{
    private readonly TResult? _result;

    private ModalDialogLoopResult(bool isCompleted, TResult? result)
    {
        IsCompleted = isCompleted;
        _result = result;
    }

    public bool IsCompleted { get; }

    public TResult Result => IsCompleted
        ? _result!
        : throw new InvalidOperationException("A continuing modal loop has no result.");

    public static ModalDialogLoopResult<TResult> Continue => new(false, default);

    public static ModalDialogLoopResult<TResult> Complete(TResult result) => new(true, result);
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
        new(false, ModalDialogLoopResult<TResult>.Continue);

    public static ModalDialogWakeResult<TResult> Changed { get; } =
        new(true, ModalDialogLoopResult<TResult>.Continue);

    public static ModalDialogWakeResult<TResult> Complete(TResult result) =>
        new(false, ModalDialogLoopResult<TResult>.Complete(result));

    public static ModalDialogWakeResult<TResult> Complete(TResult result, bool invalidate) =>
        new(invalidate, ModalDialogLoopResult<TResult>.Complete(result));
}
