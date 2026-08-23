using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class FileOperationUiRunner
{
    private readonly DialogService _dialogs;
    private readonly IFileOperationService _fileOperations;
    private readonly Func<bool> _showTotalProgress;

    public FileOperationUiRunner(ModalDialogHost _, DialogService dialogs, Func<ConsolePalette> palette, IFileOperationService fileOperations, Func<bool> showTotalProgress, FormFieldFactory fields)
    {
        _dialogs = dialogs;
        _fileOperations = fileOperations;
        _showTotalProgress = showTotalProgress;
    }

    public FileOperationResult Execute(FileOperationRequest request)
    {
        var resolver = new DialogConflictResolver(new ConflictDialog(_dialogs));
        var pauseController = new FileOperationPauseController();
        request = request with { PauseController = pauseController };
        var syncRoot = new object();
        FileOperationProgress? latestProgress = null;
        var state = new FileOperationProgressViewState(null, _showTotalProgress(), FileOperationUiStatus.Running);
        bool cancellationRequested = false;
        var content = new FileOperationProgressContent(() => state, request.Kind == FileOperationKind.Delete ? null : request.Destination);
        var cancel = FormControls.Buttons(new DialogButton("cancel", "Cancel", 'C'));
        var form = new ScrollableFormDialog();
        form.SetRows([], [cancel]);

        FileOperationResult result = _dialogs.Operation(
            new OperationDialogOptions(new CompositeDialogOptions(OperationTitle(request.Kind), 74, 18, 50, 10), TimeSpan.FromMilliseconds(120)),
            Operation,
            form,
            content,
            status: null,
            commands: new Dictionary<ConsoleKey, string> { [ConsoleKey.Escape] = "cancel" },
            synchronize: Synchronize,
            handle: Handle,
            complete: static outcome => outcome,
            onCancellationRequested: () =>
            {
                resolver.CancelPending();
                pauseController.Resume();
            });

        if (result.Cancelled)
            throw new OperationCanceledException();
        if (result.Errors.Count > 0)
            _dialogs.Message("File Operation", $"{result.FailedCount} item(s) failed. First: {result.Errors[0].Message}");
        return result;

        async Task<FileOperationResult> Operation(CancellationToken cancellationToken)
        {
            var progress = new LockedProgress<FileOperationProgress>(value => { lock (syncRoot) latestProgress = value; });
            return await _fileOperations.ExecuteAsync(request, progress, resolver, cancellationToken).ConfigureAwait(false);
        }

        bool Synchronize()
        {
            bool changed = resolver.ShowPendingConflict();
            FileOperationProgress? snapshot;
            lock (syncRoot) snapshot = latestProgress;
            var next = new FileOperationProgressViewState(snapshot, _showTotalProgress(), cancellationRequested ? FileOperationUiStatus.Stopping : FileOperationUiStatus.Running);
            changed |= state != next;
            state = next;
            return changed;
        }

        OperationDialogOutcome<FileOperationResult> Handle(CompositeDialogEvent @event)
        {
            bool isCancellationCommand = @event.Kind == CompositeDialogEventKind.Cancelled ||
                @event.Kind == CompositeDialogEventKind.Command && @event.Command == "cancel";
            if (!isCancellationCommand || cancellationRequested)
                return OperationDialogOutcome<FileOperationResult>.ContinueNoChange;

            Synchronize();
            var frame = new FileOperationProgressFrame(state.Progress, state.ShowTotalProgress, state.Status);
            bool accepted = HandleCancellation(
                frame,
                cancelImmediately: AcceptCancellation,
                requestConfirmation: () =>
                {
                    pauseController.Pause();
                    try { return new OperationCancelDialog(_dialogs).Show() && AcceptCancellation(); }
                    finally { pauseController.Resume(); }
                });
            if (!accepted)
                return OperationDialogOutcome<FileOperationResult>.ContinueNoChange;

            return frame.Progress is null || frame.Progress.Phase == FileOperationPhase.Scanning
                ? OperationDialogOutcome<FileOperationResult>.RequestImmediateCancellation
                : OperationDialogOutcome<FileOperationResult>.RequestCancellation;
        }

        bool AcceptCancellation()
        {
            cancellationRequested = true;
            state = state with { Status = FileOperationUiStatus.Stopping };
            return true;
        }
    }

    internal static bool HandleCancellation(FileOperationProgressFrame frame, Func<bool> cancelImmediately, Func<bool> requestConfirmation)
    {
        if (frame.Status != FileOperationUiStatus.Running) return false;
        return frame.Progress is null || frame.Progress.Phase == FileOperationPhase.Scanning ? cancelImmediately() : requestConfirmation();
    }

    private static string OperationTitle(FileOperationKind kind) => kind switch
    {
        FileOperationKind.Delete => "Delete",
        FileOperationKind.Move => "Move",
        FileOperationKind.Copy => "Copy",
        _ => "File operation",
    };

    private sealed class FileOperationProgressContent(Func<FileOperationProgressViewState> state, string? destination) : ICompositeDialogContent
    {
        public ICompositeDialogContentFrame CalculateFrame(Rect bounds) => new Frame(bounds);
        public void Render(IUiCanvas canvas, ICompositeDialogContentFrame rawFrame)
        {
            var frame = Require(rawFrame); var snapshot = state();
            canvas.FillRegion(frame.Bounds, FarDialogStyles.Fill);
            if (snapshot.Progress is not { } progress) { canvas.Write(frame.Bounds.X, frame.Bounds.Y, "Preparing operation...", FarDialogStyles.Fill); return; }
            Write(canvas, frame.Bounds, 0, snapshot.Status == FileOperationUiStatus.Stopping ? "Stopping..." : PhaseText(progress));
            Write(canvas, frame.Bounds, 1, progress.CurrentPath);
            if (progress.Phase == FileOperationPhase.Scanning)
            {
                Write(canvas, frame.Bounds, 3, $"Files found: {progress.ItemsDone:N0}");
                Write(canvas, frame.Bounds, 4, $"Folders found: {progress.FoldersDone:N0}");
                Write(canvas, frame.Bounds, 5, $"Bytes found: {progress.TotalBytesDone:N0}");
                return;
            }
            if (progress.Kind != FileOperationKind.Delete && progress.CurrentDestinationPath is { } target)
                Write(canvas, frame.Bounds, 2, $"to {target}");
            Write(canvas, frame.Bounds, 4, $"Files: {progress.ItemsDone:N0} / {progress.ItemsTotal:N0}");
            Write(canvas, frame.Bounds, 5, $"Bytes: {progress.TotalBytesDone:N0} / {progress.TotalBytesTotal:N0}");
            if (snapshot.ShowTotalProgress) Write(canvas, frame.Bounds, 7, $"Progress: {Percent(progress.TotalBytesDone, progress.TotalBytesTotal)}");
            if (progress.Kind != FileOperationKind.Delete)
                Write(canvas, frame.Bounds, 8, $"Destination: {progress.CurrentDestinationPath ?? destination ?? string.Empty}");
        }
        public UiInteractionFragment BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder) => UiInteractionFragment.Empty;
        public CompositeDialogContentInputResult RouteInput(ConsoleInputEvent input, ICompositeDialogContentFrame frame, UiInputRouteContext route) => CompositeDialogContentInputResult.NotHandled;
        public void ApplyCommittedFrame(ICompositeDialogContentFrame frame) { }
        private static Frame Require(ICompositeDialogContentFrame frame) => frame as Frame ?? throw new ArgumentException("Frame belongs to a different content component.", nameof(frame));
        private static void Write(IUiCanvas canvas, Rect bounds, int row, string? value) { if (row < bounds.Height) canvas.Write(bounds.X, bounds.Y + row, ConsoleTextMetrics.FitToCells(value ?? string.Empty, bounds.Width), FarDialogStyles.Fill); }
        private static string PhaseText(FileOperationProgress p) => p.Phase switch { FileOperationPhase.Scanning when p.Kind == FileOperationKind.Delete => "Scanning files for deletion", FileOperationPhase.Scanning => "Scanning the folder", FileOperationPhase.Deleting => "Deleting the file", FileOperationPhase.Validating => p.StatusMessage ?? "Validating partial file...", _ => "Copying the file" };
        private static string Percent(long done, long total) => total <= 0 ? "0%" : $"{Math.Clamp(done * 100 / total, 0, 100)}%";
        private sealed record Frame(Rect Bounds) : ICompositeDialogContentFrame;
    }

    private sealed class DialogConflictResolver(ConflictDialog dialog) : IFileOperationConflictResolver
    {
        private readonly object _gate = new(); private PendingConflict? _pending; private bool _closed;
        public bool ShowPendingConflict()
        {
            PendingConflict? pending; lock (_gate) pending = _pending; if (pending is null) return false;
            FileOperationConflictDecision decision = dialog.Show(pending.Conflict);
            lock (_gate) { if (ReferenceEquals(_pending, pending)) _pending = null; Monitor.PulseAll(_gate); }
            pending.TrySetDecision(decision); return true;
        }
        public void CancelPending()
        {
            PendingConflict? pending; lock (_gate) { _closed = true; pending = _pending; _pending = null; Monitor.PulseAll(_gate); }
            pending?.TrySetDecision(FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel));
        }
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            var pending = new PendingConflict(conflict); lock (_gate)
            {
                if (_closed) return FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel);
                while (_pending is not null) { Monitor.Wait(_gate); if (_closed) return FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel); }
                _pending = pending; Monitor.PulseAll(_gate);
            }
            return pending.WaitForDecision();
        }
        private sealed class PendingConflict(FileOperationConflict conflict)
        {
            private readonly ManualResetEventSlim _ready = new(); private readonly object _gate = new(); private FileOperationConflictDecision? _decision;
            public FileOperationConflict Conflict { get; } = conflict;
            public bool TrySetDecision(FileOperationConflictDecision decision) { lock (_gate) { if (_decision is not null) return false; _decision = decision; _ready.Set(); return true; } }
            public FileOperationConflictDecision WaitForDecision() { _ready.Wait(); return _decision ?? throw new InvalidOperationException("Conflict dialog closed without a decision."); }
        }
    }
    private sealed class LockedProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed record FileOperationProgressViewState(FileOperationProgress? Progress, bool ShowTotalProgress, FileOperationUiStatus Status);
    internal sealed record FileOperationProgressFrame(FileOperationProgress? Progress, bool ShowTotalProgress, FileOperationUiStatus Status);
    internal enum FileOperationUiStatus { Running, Stopping, Completed, Failed }
    private sealed class FileOperationPauseController : IFileOperationPauseController { private readonly ManualResetEventSlim _canRun = new(true); public void Pause() => _canRun.Reset(); public void Resume() => _canRun.Set(); public void WaitIfPaused(CancellationToken cancellationToken) => _canRun.Wait(cancellationToken); }
}
