using CSharpFar.Console;

namespace CSharpFar.Ui;

/// <summary>Semantic transition requested by an operation dialog input handler.</summary>
public enum OperationDialogAction
{
    ContinueNoChange,
    ContinueChanged,
    RequestCancellation,
    RequestImmediateCancellation,
    Complete,
}

/// <summary>Semantic result of an operation-dialog command.</summary>
public readonly record struct OperationDialogOutcome<TResult>(OperationDialogAction Action, TResult? Result = default)
{
    public static OperationDialogOutcome<TResult> ContinueNoChange => new(OperationDialogAction.ContinueNoChange);
    public static OperationDialogOutcome<TResult> ContinueChanged => new(OperationDialogAction.ContinueChanged);
    public static OperationDialogOutcome<TResult> RequestCancellation => new(OperationDialogAction.RequestCancellation);
    public static OperationDialogOutcome<TResult> RequestImmediateCancellation => new(OperationDialogAction.RequestImmediateCancellation);
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
        Action? onCancellationRequested = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(complete);
        return RunCore(
            options,
            operation,
            form,
            content,
            status,
            commands,
            synchronize,
            handle,
            result => OperationDialogOutcome<TResult>.Complete(complete(result)),
            waitForOperationOnClose: true,
            onCancellationRequested,
            cancellationToken);
    }

    internal TResult RunPersistent<TBackground, TResult>(
        OperationDialogOptions options,
        Func<CancellationToken, Task<TBackground>> operation,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        IReadOnlyDictionary<ConsoleKey, string>? commands,
        Func<bool>? synchronize,
        Func<CompositeDialogEvent, OperationDialogOutcome<TResult>> handle,
        Func<TBackground, OperationDialogOutcome<TResult>> operationCompleted,
        Action? onCancellationRequested = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationCompleted);
        return RunCore(
            options,
            operation,
            form,
            content,
            status,
            commands,
            synchronize,
            handle,
            operationCompleted,
            waitForOperationOnClose: false,
            onCancellationRequested,
            cancellationToken);
    }

    private TResult RunCore<TBackground, TResult>(
        OperationDialogOptions options,
        Func<CancellationToken, Task<TBackground>> operation,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        IReadOnlyDictionary<ConsoleKey, string>? commands,
        Func<bool>? synchronize,
        Func<CompositeDialogEvent, OperationDialogOutcome<TResult>> handle,
        Func<TBackground, OperationDialogOutcome<TResult>> operationCompleted,
        bool waitForOperationOnClose,
        Action? onCancellationRequested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(handle);
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

        _ = synchronize?.Invoke();
        bool cancellationNotified = false;
        bool cancellationPending = false;
        bool operationCompletionHandled = false;

        void RequestCancellation()
        {
            operationCancellation.Cancel();
            if (!cancellationNotified)
            {
                cancellationNotified = true;
                onCancellationRequested?.Invoke();
            }
        }

        void ObserveOnClose()
        {
            if (waitForOperationOnClose)
                ObserveBeforeClose(task);
            else
                ObserveEventually(task);
        }

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
                            cancellationPending = true;
                            return ModalDialogLoopResult<TResult>.ContinueChanged;
                        case OperationDialogAction.RequestImmediateCancellation:
                            RequestCancellation();
                            return ModalDialogLoopResult<TResult>.ContinueChanged;
                        case OperationDialogAction.Complete:
                            RequestCancellation();
                            ObserveOnClose();
                            return ModalDialogLoopResult<TResult>.Complete(outcome.Result!);
                        default:
                            return ModalDialogLoopResult<TResult>.ContinueNoChange;
                    }
                },
                () => operationCompletionHandled
                    ? null
                    : DateTimeOffset.UtcNow + options.RefreshInterval,
                () =>
                {
                    bool changed = synchronize?.Invoke() ?? false;
                    if (!task.IsCompleted || operationCompletionHandled)
                        return changed ? ModalDialogWakeResult<TResult>.Changed : ModalDialogWakeResult<TResult>.NoChange;

                    TBackground result = task.GetAwaiter().GetResult();
                    operationCompletionHandled = true;
                    OperationDialogOutcome<TResult> outcome = operationCompleted(result);
                    switch (outcome.Action)
                    {
                        case OperationDialogAction.Complete:
                            return ModalDialogWakeResult<TResult>.Complete(outcome.Result!, true);
                        case OperationDialogAction.ContinueChanged:
                            _ = synchronize?.Invoke();
                            return ModalDialogWakeResult<TResult>.Changed;
                        case OperationDialogAction.RequestCancellation:
                        case OperationDialogAction.RequestImmediateCancellation:
                            RequestCancellation();
                            return ModalDialogWakeResult<TResult>.Changed;
                        default:
                            return changed
                                ? ModalDialogWakeResult<TResult>.Changed
                                : ModalDialogWakeResult<TResult>.NoChange;
                    }
                },
                prepareRender: null,
                afterFrameCommitted: () =>
                {
                    if (!cancellationPending)
                        return;

                    cancellationPending = false;
                    RequestCancellation();
                },
                cancellationToken,
                completionWake.Token);
        }
        catch
        {
            RequestCancellation();
            ObserveOnClose();
            throw;
        }
    }

    private static void ObserveEventually(Task task)
    {
        if (task.IsCompleted)
        {
            ObserveBeforeClose(task);
            return;
        }

        _ = task.ContinueWith(
            static completed => ObserveBeforeClose(completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveBeforeClose(Task task)
    {
        try { task.GetAwaiter().GetResult(); }
        catch { }
    }
}
