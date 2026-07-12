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
