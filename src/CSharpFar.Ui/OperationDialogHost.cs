using CSharpFar.Console;

namespace CSharpFar.Ui;

/// <summary>Semantic transition requested by an operation dialog input handler.</summary>
public enum OperationDialogAction
{
    ContinueNoChange,
    ContinueChanged,
    RequestCancellation,
    Complete,
}

/// <summary>Semantic result of an operation-dialog command.</summary>
public readonly record struct OperationDialogOutcome<TResult>(OperationDialogAction Action, TResult? Result = default)
{
    public static OperationDialogOutcome<TResult> ContinueNoChange => new(OperationDialogAction.ContinueNoChange);
    public static OperationDialogOutcome<TResult> ContinueChanged => new(OperationDialogAction.ContinueChanged);
    public static OperationDialogOutcome<TResult> RequestCancellation => new(OperationDialogAction.RequestCancellation);
    public static OperationDialogOutcome<TResult> Complete(TResult result) => new(OperationDialogAction.Complete, result);
}

/// <summary>Configuration for the standard long-running modal-operation lifecycle.</summary>
public sealed record OperationDialogOptions(
    CompositeDialogOptions Dialog,
    TimeSpan RefreshInterval)
{
    public OperationDialogOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Dialog);
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval));
        return this;
    }
}

/// <summary>Owns the UI lifecycle around one cancellable background operation.</summary>
public sealed class OperationDialogHost
{
    private readonly CompositeDialogHost _composite;

    public OperationDialogHost(ModalDialogHost modalDialogs) =>
        _composite = new CompositeDialogHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public TResult Run<TBackground, TResult>(
        OperationDialogOptions options,
        Func<CancellationToken, Task<TBackground>> operation,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        IReadOnlyDictionary<ConsoleKey, string>? commands,
        Func<bool>? synchronize,
        Func<CompositeDialogEvent, OperationDialogOutcome<TResult>> handle,
        Func<TBackground, TResult> complete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(complete);
        options.Validate();

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var completionWake = new CancellationTokenSource();
        Task<TBackground> task = Task.Run(() => operation(operationCancellation.Token), CancellationToken.None);
        _ = task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Cancel(),
            completionWake,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return _composite.RunTimed(
                options.Dialog,
                form,
                content,
                status,
                commands,
                semantic =>
                {
                    OperationDialogOutcome<TResult> outcome = handle(semantic);
                    switch (outcome.Action)
                    {
                        case OperationDialogAction.ContinueChanged:
                            return ModalDialogLoopResult<TResult>.ContinueChanged;
                        case OperationDialogAction.RequestCancellation:
                            operationCancellation.Cancel();
                            return ModalDialogLoopResult<TResult>.ContinueChanged;
                        case OperationDialogAction.Complete:
                            operationCancellation.Cancel();
                            ObserveBeforeClose(task);
                            return ModalDialogLoopResult<TResult>.Complete(outcome.Result!);
                        default:
                            return ModalDialogLoopResult<TResult>.ContinueNoChange;
                    }
                },
                () => DateTimeOffset.UtcNow + options.RefreshInterval,
                () =>
                {
                    bool changed = synchronize?.Invoke() ?? false;
                    if (!task.IsCompleted)
                        return changed ? ModalDialogWakeResult<TResult>.Changed : ModalDialogWakeResult<TResult>.NoChange;

                    TBackground result = task.GetAwaiter().GetResult();
                    return ModalDialogWakeResult<TResult>.Complete(complete(result), changed);
                },
                prepareRender: synchronize is null ? null : () => _ = synchronize(),
                cancellationToken,
                completionWake.Token);
        }
        catch
        {
            operationCancellation.Cancel();
            ObserveBeforeClose(task);
            throw;
        }
    }

    private static void ObserveBeforeClose(Task task)
    {
        try { task.GetAwaiter().GetResult(); }
        catch { }
    }
}
