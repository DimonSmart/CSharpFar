using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

/// <summary>
/// Owns directory load requests on the UI thread. Workers only prepare immutable
/// request snapshots; completions are applied through the host's UI queue.
/// At most two requests run and one latest request waits per logical panel.
/// </summary>
public sealed class PanelLoadingCoordinator(
    PanelController controller,
    FilePanelSourceRegistry sources,
    Action<Action> postToUi,
    Action<Exception>? reportError = null) : IDisposable
{
    private readonly Dictionary<FilePanelState, Slot> _panels = new();
    private volatile bool _disposed;

    public void Navigate(FilePanelState panel, PanelLocation location,
        AppSettings.PanelOptionsSettings options, bool preserveOnError = false,
        Action<bool>? completed = null) =>
        RequestLoad(panel, location, options, refresh: false, preserveOnError, null, completed);

    public void Refresh(FilePanelState panel, AppSettings.PanelOptionsSettings options,
        Func<int> visibleRows, Action<bool>? completed = null)
    {
        if (_disposed || panel.ContentKind != PanelContentKind.Source)
            return;
        if (_panels.TryGetValue(panel, out var slot) && slot.Current is { } current)
        {
            // Navigation wins. A refresh arriving during another refresh is
            // represented by one follow-up, not another queue entry.
            if (panel.SortMode != current.ViewRequest.SortMode ||
                panel.SortDescending != current.ViewRequest.SortDescending)
            {
                RequestLoad(panel, current.Location, options, current.Refresh, current.PreserveOnError,
                    current.VisibleRows, current.Completed);
                return;
            }
            if (current.Refresh)
                slot.RefreshAgain = () => Refresh(panel, options, visibleRows, completed);
            return;
        }
        RequestLoad(panel, panel.CurrentLocation, options, refresh: true, false, visibleRows, completed);
    }

    public bool Cancel(FilePanelState panel)
    {
        if (!_panels.TryGetValue(panel, out var slot) || slot.Current is null)
            return false;
        slot.RefreshAgain = null;
        CancelCurrent(slot);
        panel.IsLoading = false;
        panel.IsLoadingIndicatorVisible = false;
        return true;
    }

    private void RequestLoad(FilePanelState panel, PanelLocation location,
        AppSettings.PanelOptionsSettings options, bool refresh, bool preserveOnError,
        Func<int>? visibleRows, Action<bool>? completed)
    {
        if (_disposed)
            return;
        if (!controller.TryNormalizeLocation(location, out var normalized) ||
            !sources.TryAcquireUsage(normalized.SourceId, out var source, out var lease))
        {
            completed?.Invoke(false);
            return;
        }

        if (!_panels.TryGetValue(panel, out var slot))
            _panels.Add(panel, slot = new Slot());

        // Hold the new lease before cancelling the previous request: a temporary
        // source may have been scheduled for retirement while it is not visible.
        var request = new LoadRequest(normalized, source, panel.ContentRevision,
            new PanelViewRequest
            {
                Location = normalized,
                DirectoryPath = normalized.SourcePath,
                SortMode = panel.SortMode,
                SortDescending = panel.SortDescending,
                Options = Snapshot(options),
                SelectedLocations = refresh
                    ? new HashSet<PanelLocation>(panel.SelectedLocations, panel.SelectedLocations.Comparer)
                    : new HashSet<PanelLocation>(),
            }, refresh, preserveOnError, visibleRows, completed, lease, ReportError);

        slot.RefreshAgain = null;
        CancelCurrent(slot);
        slot.Current = request;
        panel.IsLoading = true;
        _ = ShowIndicatorAsync(panel, slot, request, request.Token);
        if (slot.Active.Count < 2)
            Start(panel, slot, request);
        else
            slot.Pending = request;
    }

    private async Task ShowIndicatorAsync(FilePanelState panel, Slot slot, LoadRequest request, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), token).ConfigureAwait(false);
            if (_disposed || request.WorkerFinished)
                return;
            postToUi(() =>
            {
                if (!_disposed && ReferenceEquals(slot.Current, request) && !request.WorkerFinished)
                    panel.IsLoadingIndicatorVisible = true;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportError(exception); }
    }

    private static AppSettings.PanelOptionsSettings Snapshot(AppSettings.PanelOptionsSettings options) =>
        new()
        {
            ShowHiddenAndSystemFiles = options.ShowHiddenAndSystemFiles,
            SelectFolders = options.SelectFolders,
            SortFoldersByExtension = options.SortFoldersByExtension,
            DetectVolumeMountPoints = options.DetectVolumeMountPoints,
            ShowFreeSize = options.ShowFreeSize,
            ShowParentDirectoryInRootFolders = options.ShowParentDirectoryInRootFolders,
        };

    private static void CancelCurrent(Slot slot)
    {
        var current = slot.Current;
        slot.Current = null;
        if (current is null)
            return;
        current.Cancel();
        if (ReferenceEquals(slot.Pending, current))
        {
            slot.Pending = null;
            current.FinishWorker();
        }
    }

    private void Start(FilePanelState panel, Slot slot, LoadRequest request)
    {
        slot.Active.Add(request);
        _ = Task.Run(() => PrepareAsync(panel, slot, request));
    }

    private async Task PrepareAsync(FilePanelState panel, Slot slot, LoadRequest request)
    {
        PanelView? view = null;
        Exception? error = null;
        try
        {
            view = await controller.PrepareViewAsync(request.ViewRequest, request.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            request.FinishWorker();
        }

        if (_disposed)
            return;
        try
        {
            postToUi(() => Complete(panel, slot, request, view, error));
        }
        catch (Exception exception)
        {
            ReportError(exception);
            request.FinishUi();
        }
    }

    private void Complete(FilePanelState panel, Slot slot, LoadRequest request, PanelView? view, Exception? error)
    {
        if (_disposed)
            return;
        slot.Active.Remove(request);
        if (ReferenceEquals(slot.Current, request))
        {
            slot.Current = null;
            panel.IsLoading = false;
            panel.IsLoadingIndicatorVisible = false;
            var refreshAgain = slot.RefreshAgain;
            slot.RefreshAgain = null;
            try
            {
                bool applicable = !request.Token.IsCancellationRequested &&
                    panel.ContentRevision == request.ContentRevision &&
                    panel.SortMode == request.ViewRequest.SortMode &&
                    panel.SortDescending == request.ViewRequest.SortDescending &&
                    sources.TryGetSource(request.Location.SourceId, out var currentSource) &&
                    ReferenceEquals(currentSource, request.Source);
                if (applicable && error is not OperationCanceledException)
                {
                    if (error is null && view is not null)
                    {
                        sources.RetainSource(request.Location.SourceId);
                        if (request.Refresh)
                            controller.ApplyRefreshedView(panel, view, request.VisibleRows!());
                        else
                            controller.ApplyLoadedLocation(panel, request.Location, view);
                        request.Completed?.Invoke(true);
                    }
                    else
                    {
                        if (error is not null)
                            ReportError(error);
                        if (!request.PreserveOnError && error is not null &&
                            sources.TryGetSource(request.Location.SourceId, out currentSource) &&
                            ReferenceEquals(currentSource, request.Source))
                        {
                            sources.RetainSource(request.Location.SourceId);
                            PanelController.ApplyLoadError(panel, request.Location, error);
                        }
                        request.Completed?.Invoke(false);
                    }
                }
            }
            finally
            {
                request.FinishUi();
            }

            if (slot.Current is null && panel.CurrentLocation == request.Location)
                refreshAgain?.Invoke();
        }

        if (slot.Pending is { } pending && slot.Active.Count < 2)
        {
            slot.Pending = null;
            Start(panel, slot, pending);
        }

        if (slot.Current is null && slot.Active.Count == 0 && slot.Pending is null)
            _panels.Remove(panel);
    }

    private void ReportError(Exception exception)
    {
        try { reportError?.Invoke(exception); }
        catch { /* Diagnostics must not fault a worker or the UI loop. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (panel, slot) in _panels)
        {
            CancelCurrent(slot);
            panel.IsLoading = false;
            panel.IsLoadingIndicatorVisible = false;
        }
        _panels.Clear();
        // Do not wait for uncooperative legacy providers at shutdown. Their
        // leases are released only after the actual worker and cancellation finish.
    }

    private sealed class Slot
    {
        public List<LoadRequest> Active { get; } = [];
        public LoadRequest? Current { get; set; }
        public LoadRequest? Pending { get; set; }
        public Action? RefreshAgain { get; set; }
    }

    private sealed class LoadRequest(
        PanelLocation location, IFilePanelSource source, long revision, PanelViewRequest viewRequest,
        bool refresh, bool preserveOnError, Func<int>? visibleRows, Action<bool>? completed,
        IDisposable lease, Action<Exception> reportError)
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _workerFinished;
        private int _uiFinished;
        private int _remaining = 2;

        public PanelLocation Location { get; } = location;
        public IFilePanelSource Source { get; } = source;
        public long ContentRevision { get; } = revision;
        public PanelViewRequest ViewRequest { get; } = viewRequest;
        public bool Refresh { get; } = refresh;
        public bool PreserveOnError { get; } = preserveOnError;
        public Func<int>? VisibleRows { get; } = visibleRows;
        public Action<bool>? Completed { get; } = completed;
        public CancellationToken Token => _cancellation.Token;
        public bool WorkerFinished => Volatile.Read(ref _workerFinished) != 0;

        public void Cancel() => _ = CancelAsync();

        private async Task CancelAsync()
        {
            try { await _cancellation.CancelAsync().ConfigureAwait(false); }
            catch (Exception exception) { reportError(exception); }
            finally { FinishUi(); }
        }

        public void FinishWorker()
        {
            if (Interlocked.Exchange(ref _workerFinished, 1) == 0)
                Finish();
        }

        public void FinishUi()
        {
            if (Interlocked.Exchange(ref _uiFinished, 1) == 0)
                Finish();
        }

        private void Finish()
        {
            if (Interlocked.Decrement(ref _remaining) != 0) return;
            _cancellation.Dispose();
            try { lease.Dispose(); }
            catch (Exception exception) { reportError(exception); }
        }
    }
}
