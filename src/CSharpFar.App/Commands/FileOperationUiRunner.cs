using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class FileOperationUiRunner
{
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly IFileOperationService _fileOperations;
    private readonly Func<bool> _showTotalProgress;
    private readonly Func<ConsoleKeyInfo?> _tryReadConsoleKey;

    public FileOperationUiRunner(
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        IFileOperationService fileOperations,
        Func<bool> showTotalProgress,
        Func<ConsoleKeyInfo?> tryReadConsoleKey)
    {
        _screen = screen;
        _palette = palette;
        _fileOperations = fileOperations;
        _showTotalProgress = showTotalProgress;
        _tryReadConsoleKey = tryReadConsoleKey;
    }

    public FileOperationResult Execute(FileOperationRequest request)
    {
        _screen.SetCursorVisible(false);
        var progressDialog = new ProgressDialog(_screen, request.Destination ?? string.Empty);
        var conflictDialog = new ConflictDialog(_screen, _palette());
        var cancelDialog = new OperationCancelDialog(_screen);
        var resolver = new DialogConflictResolver(conflictDialog);
        var pauseController = new FileOperationPauseController();
        request = request with { PauseController = pauseController };
        using var cts = new CancellationTokenSource();

        FileOperationProgress? latestProgress = null;
        var progress = new Progress<FileOperationProgress>(p =>
        {
            latestProgress = p;
        });

        FileOperationResult? completedResult = null;
        Exception? completedException = null;
        Task task = Task.Run(async () =>
        {
            try
            {
                completedResult = await _fileOperations.ExecuteAsync(request, progress, resolver, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                completedException = ex;
            }
        }, cts.Token);

        FileOperationProgress? renderedProgress = null;
        var lastRender = DateTime.MinValue;

        while (!task.IsCompleted)
        {
            if (resolver.ShowPendingConflict())
            {
                renderedProgress = null;
                lastRender = DateTime.MinValue;
                continue;
            }

            if (latestProgress is not null &&
                !ReferenceEquals(latestProgress, renderedProgress) &&
                DateTime.UtcNow - lastRender >= TimeSpan.FromMilliseconds(120))
            {
                progressDialog.Update(latestProgress, _showTotalProgress());
                renderedProgress = latestProgress;
                lastRender = DateTime.UtcNow;
            }

            var key = _tryReadConsoleKey();
            if (key?.Key == ConsoleKey.Escape)
            {
                if (latestProgress?.Phase == FileOperationPhase.Scanning)
                {
                    cts.Cancel();
                }
                else
                {
                    pauseController.Pause();
                    try
                    {
                        if (cancelDialog.Show())
                            cts.Cancel();
                    }
                    finally
                    {
                        pauseController.Resume();
                    }
                }

                renderedProgress = null;
                lastRender = DateTime.MinValue;
            }

            Thread.Sleep(30);
        }

        try
        {
            if (latestProgress is not null && !ReferenceEquals(latestProgress, renderedProgress))
                progressDialog.Update(latestProgress, _showTotalProgress());

            if (task.IsCanceled)
                throw new OperationCanceledException();
            if (completedException is not null)
                throw completedException;

            FileOperationResult result = completedResult
                ?? throw new InvalidOperationException("File operation did not return a result.");
            if (result.Cancelled)
                throw new OperationCanceledException();
            if (result.Errors.Count > 0)
                new MessageDialog(_screen).Show(
                    "File Operation",
                    $"{result.FailedCount} item(s) failed. First: {result.Errors[0].Message}");

            return result;
        }
        finally
        {
            _screen.SetCursorVisible(false);
        }
    }

    private sealed class DialogConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDialog _dialog;
        private readonly object _gate = new();
        private PendingConflict? _pendingConflict;

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

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            var pendingConflict = new PendingConflict(conflict);

            lock (_gate)
            {
                while (_pendingConflict is not null)
                    Monitor.Wait(_gate);

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

    private sealed class FileOperationPauseController : IFileOperationPauseController
    {
        private readonly ManualResetEventSlim _canRun = new(initialState: true);

        public void Pause() => _canRun.Reset();

        public void Resume() => _canRun.Set();

        public void WaitIfPaused(CancellationToken cancellationToken) =>
            _canRun.Wait(cancellationToken);
    }
}
