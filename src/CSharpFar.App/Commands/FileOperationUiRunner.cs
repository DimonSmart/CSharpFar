using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class FileOperationUiRunner
{
    private const int RedrawDelayMilliseconds = 120;
    private static readonly UiTargetId ProgressKeyboardTarget = new("file-operation.progress");

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogHost _modalDialogs;
    private readonly Func<ConsolePalette> _palette;
    private readonly IFileOperationService _fileOperations;
    private readonly Func<bool> _showTotalProgress;

    public FileOperationUiRunner(
        ScreenRenderer screen,
        ModalDialogHost modalDialogs,
        Func<ConsolePalette> palette,
        IFileOperationService fileOperations,
        Func<bool> showTotalProgress)
    {
        _screen = screen;
        _modalDialogs = modalDialogs;
        _palette = palette;
        _fileOperations = fileOperations;
        _showTotalProgress = showTotalProgress;
    }

    public FileOperationResult Execute(FileOperationRequest request)
    {
        var progressDialog = new ProgressDialog(_screen, request.Destination ?? string.Empty);
        var conflictDialog = new ConflictDialog(_modalDialogs, _palette());
        var cancelDialog = new OperationCancelDialog(_modalDialogs);
        using var cts = new CancellationTokenSource();
        var resolver = new DialogConflictResolver(conflictDialog);
        var pauseController = new FileOperationPauseController();
        request = request with { PauseController = pauseController };

        var syncRoot = new object();
        FileOperationProgress? latestProgress = null;
        var visibleState = new FileOperationProgressViewState(null, _showTotalProgress(), FileOperationUiStatus.Running);
        bool cancellationRequested = false;

        var progress = new LockedProgress<FileOperationProgress>(p =>
        {
            lock (syncRoot)
                latestProgress = p;
        });

        Task<FileOperationBackgroundOutcome> operationTask = Task.Run(async () =>
        {
            try
            {
                FileOperationResult result = await _fileOperations.ExecuteAsync(request, progress, resolver, cts.Token)
                    .ConfigureAwait(false);
                return new FileOperationBackgroundOutcome(result, null);
            }
            catch (Exception ex)
            {
                return new FileOperationBackgroundOutcome(null, ex);
            }
        });

        using var completionWake = new CancellationTokenSource();
        _ = operationTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Cancel(),
            completionWake,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        FileOperationBackgroundOutcome outcome;
        try
        {
            outcome = _modalDialogs.RunInteractiveTimed<FileOperationProgressFrame, FileOperationProgressInput, FileOperationBackgroundOutcome>(
                (context, _) => RenderProgressFrame(context, progressDialog, visibleState),
                BuildInteractionFrame,
                RouteInput,
                (_, input) => HandleInput(input),
                getNextWakeUtc: () => DateTimeOffset.UtcNow.AddMilliseconds(RedrawDelayMilliseconds),
                handleWake: HandleWake,
                prepareRender: () => SynchronizeVisibleState(ReadLatestProgress()),
                wakeSignal: completionWake.Token);
        }
        catch (Exception)
        {
            TryCancel(cts);
            resolver.CancelPending();
            ObserveOperationTaskAfterUiException(operationTask);
            pauseController.Resume();
            throw;
        }

        if (outcome.Exception is not null)
            throw outcome.Exception;

        FileOperationResult result = outcome.Result
            ?? throw new InvalidOperationException("File operation did not return a result.");
        if (result.Cancelled)
            throw new OperationCanceledException();
        if (result.Errors.Count > 0)
            new MessageDialog(_modalDialogs).Show(
                "File Operation",
                $"{result.FailedCount} item(s) failed. First: {result.Errors[0].Message}");

        return result;

        ModalDialogWakeResult<FileOperationBackgroundOutcome> HandleWake(FileOperationProgressFrame frame)
        {
            bool changed = false;
            if (!operationTask.IsCompleted && resolver.ShowPendingConflict())
                changed = true;

            if (!operationTask.IsCompleted)
            {
                changed |= SynchronizeVisibleState(ReadLatestProgress());
                return changed
                    ? ModalDialogWakeResult<FileOperationBackgroundOutcome>.Changed
                    : ModalDialogWakeResult<FileOperationBackgroundOutcome>.NoChange;
            }

            FileOperationBackgroundOutcome final = operationTask.GetAwaiter().GetResult();
            FileOperationUiStatus status = final.Exception is null ? FileOperationUiStatus.Completed : FileOperationUiStatus.Failed;
            changed |= SynchronizeVisibleState(ReadLatestProgress(), status);
            return ModalDialogWakeResult<FileOperationBackgroundOutcome>.Complete(final, invalidate: changed);
        }

        ModalDialogLoopResult<FileOperationBackgroundOutcome> HandleInput(FileOperationProgressInput input)
        {
            if (input != FileOperationProgressInput.CancelRequested || cancellationRequested)
                return ModalDialogLoopResult<FileOperationBackgroundOutcome>.Continue;

            FileOperationProgress? progressSnapshot = visibleState.Progress;
            if (progressSnapshot is null || progressSnapshot.Phase == FileOperationPhase.Scanning)
            {
                cancellationRequested = true;
                visibleState = visibleState with { Status = FileOperationUiStatus.Stopping };
                TryCancel(cts);
                resolver.CancelPending();
                return ModalDialogLoopResult<FileOperationBackgroundOutcome>.Continue;
            }

            pauseController.Pause();
            try
            {
                if (cancelDialog.Show())
                {
                    cancellationRequested = true;
                    visibleState = visibleState with { Status = FileOperationUiStatus.Stopping };
                    TryCancel(cts);
                    resolver.CancelPending();
                }
            }
            finally
            {
                pauseController.Resume();
            }

            return ModalDialogLoopResult<FileOperationBackgroundOutcome>.Continue;
        }

        FileOperationProgress? ReadLatestProgress()
        {
            lock (syncRoot)
                return latestProgress;
        }

        bool SynchronizeVisibleState(FileOperationProgress? progressSnapshot, FileOperationUiStatus? status = null)
        {
            var next = new FileOperationProgressViewState(
                progressSnapshot,
                _showTotalProgress(),
                status ?? (cancellationRequested ? FileOperationUiStatus.Stopping : FileOperationUiStatus.Running));
            bool changed = visibleState != next;
            visibleState = next;
            return changed;
        }
    }

    private static FileOperationProgressFrame RenderProgressFrame(
        UiRenderContext context,
        ProgressDialog dialog,
        FileOperationProgressViewState state)
    {
        if (state.Progress is { } progress)
            dialog.Render(context, progress, state.ShowTotalProgress);

        return new FileOperationProgressFrame(state.Progress, state.ShowTotalProgress, state.Status);
    }

    private static UiInteractionFrame BuildInteractionFrame(FileOperationProgressFrame frame) =>
        new(
            [],
            new UiFocusFrame([new UiFocusEntry(ProgressKeyboardTarget, 0)], ProgressKeyboardTarget),
            ProgressKeyboardTarget);

    private static (FileOperationProgressInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        FileOperationProgressFrame frame,
        UiInputRouteContext route)
    {
        return input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape }
            ? (FileOperationProgressInput.CancelRequested, UiInputResult.HandledResult)
            : (FileOperationProgressInput.None, UiInputResult.NotHandled);
    }

    private static void ObserveOperationTaskAfterUiException(Task<FileOperationBackgroundOutcome> operationTask)
    {
        _ = operationTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception)
        {
        }
    }

    private sealed class DialogConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDialog _dialog;
        private readonly object _gate = new();
        private PendingConflict? _pendingConflict;
        private bool _closed;

        public DialogConflictResolver(ConflictDialog dialog)
        {
            _dialog = dialog;
        }

        public bool ShowPendingConflict()
        {
            PendingConflict? pendingConflict;
            lock (_gate)
            {
                pendingConflict = _pendingConflict;
            }

            if (pendingConflict is null)
                return false;

            var decision = _dialog.Show(pendingConflict.Conflict);

            lock (_gate)
            {
                if (ReferenceEquals(_pendingConflict, pendingConflict))
                    _pendingConflict = null;
                Monitor.PulseAll(_gate);
            }

            pendingConflict.SetDecision(decision);
            return true;
        }

        public void CancelPending()
        {
            PendingConflict? pendingConflict;
            lock (_gate)
            {
                _closed = true;
                pendingConflict = _pendingConflict;
                _pendingConflict = null;
                Monitor.PulseAll(_gate);
            }

            pendingConflict?.SetDecision(FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel));
        }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            var pendingConflict = new PendingConflict(conflict);

            lock (_gate)
            {
                if (_closed)
                    return FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel);

                while (_pendingConflict is not null)
                {
                    Monitor.Wait(_gate);
                    if (_closed)
                        return FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel);
                }

                _pendingConflict = pendingConflict;
                Monitor.PulseAll(_gate);
            }

            return pendingConflict.WaitForDecision();
        }

        private sealed class PendingConflict
        {
            private readonly ManualResetEventSlim _decisionReady = new();
            private FileOperationConflictDecision? _decision;

            public PendingConflict(FileOperationConflict conflict)
            {
                Conflict = conflict;
            }

            public FileOperationConflict Conflict { get; }

            public void SetDecision(FileOperationConflictDecision decision)
            {
                _decision = decision;
                _decisionReady.Set();
            }

            public FileOperationConflictDecision WaitForDecision()
            {
                _decisionReady.Wait();
                return _decision
                    ?? throw new InvalidOperationException("Conflict dialog closed without a decision.");
            }
        }
    }

    private sealed class LockedProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed record FileOperationBackgroundOutcome(
        FileOperationResult? Result,
        Exception? Exception);

    private sealed record FileOperationProgressViewState(
        FileOperationProgress? Progress,
        bool ShowTotalProgress,
        FileOperationUiStatus Status);

    private sealed record FileOperationProgressFrame(
        FileOperationProgress? Progress,
        bool ShowTotalProgress,
        FileOperationUiStatus Status);

    private enum FileOperationUiStatus
    {
        Running,
        Stopping,
        Completed,
        Failed,
    }

    private enum FileOperationProgressInput
    {
        None,
        CancelRequested,
    }

    private sealed class FileOperationPauseController : IFileOperationPauseController
    {
        private readonly ManualResetEventSlim _canRun = new(initialState: true);

        public void Pause() => _canRun.Reset();

        public void Resume() => _canRun.Set();

        public void WaitIfPaused(CancellationToken cancellationToken) =>
            _canRun.Wait(cancellationToken);
    }
}
