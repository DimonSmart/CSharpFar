using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.App.Panels;

/// <summary>
/// Prepares remote/plugin panel views outside the terminal thread. All panel
/// mutations and success/failure callbacks run through the application UI queue.
/// A newer request supersedes an older request for the same panel state.
/// </summary>
internal sealed class AsyncPluginPanelLoader
{
    private readonly PanelController _controller;
    private readonly FilePanelSourceRegistry _sources;
    private readonly Action<Action> _postToUi;
    private readonly Action<Exception>? _reportError;
    private readonly object _gate = new();
    private readonly Dictionary<FilePanelState, Pending> _pending = new(ReferenceEqualityComparer.Instance);
    private bool _stopping;

    public AsyncPluginPanelLoader(
        PanelController controller, FilePanelSourceRegistry sources,
        Action<Action> postToUi, Action<Exception>? reportError = null)
    {
        _controller = controller;
        _sources = sources;
        _postToUi = postToUi;
        _reportError = reportError;
    }

    public static bool CanLoad(PanelLocation location) => location.SourceId != PanelSourceId.Local;

    public void Load(
        FilePanelState state, PanelLocation location, AppSettings.PanelOptionsSettings options,
        Action? onSuccess = null, Action<Exception>? onFailure = null,
        Action? onAbandoned = null)
    {
        Begin(state, location, options, refresh: false, visibleRows: 0,
            onSuccess, onFailure, onAbandoned);
    }

    public void Refresh(
        FilePanelState state, int visibleRows, AppSettings.PanelOptionsSettings options,
        Action? onSuccess = null, Action<Exception>? onFailure = null)
    {
        Begin(state, state.CurrentLocation, options, refresh: true, visibleRows,
            onSuccess, onFailure, onAbandoned: null);
    }

    /// <summary>Invalidates a pending load before a synchronous navigation replaces the panel.</summary>
    public void CancelPending(FilePanelState state)
    {
        Pending? previous = null;
        lock (_gate)
        {
            if (_pending.Remove(state, out var pending))
                previous = pending;
        }
        if (previous is not null)
            RequestCancellation(previous);
    }

    private void Begin(
        FilePanelState state, PanelLocation location, AppSettings.PanelOptionsSettings options,
        bool refresh, int visibleRows,
        Action? onSuccess, Action<Exception>? onFailure, Action? onAbandoned)
    {
        if (!CanLoad(location))
            throw new ArgumentException("Background loading is reserved for non-local sources.", nameof(location));

        var lease = _sources.AcquireUsage(location.SourceId);
        var request = new Pending(
            state, state.CurrentLocation, location, options, refresh, visibleRows,
            onSuccess, onFailure, onAbandoned, lease);
        Pending? previous = null;
        lock (_gate)
        {
            if (_stopping)
            {
                lease.Dispose();
                request.Cancellation.Dispose();
                return;
            }

            if (_pending.TryGetValue(state, out previous))
                _pending.Remove(state);
            _pending.Add(state, request);
        }
        if (previous is not null)
            RequestCancellation(previous);
        _ = PrepareAndPostAsync(request);
    }

    private async Task PrepareAndPostAsync(Pending request)
    {
        PanelView? view = null;
        Exception? failure = null;
        try
        {
            view = request.Refresh
                ? await _controller.PrepareRefreshAsync(request.State, request.Options,
                    request.Cancellation.Token).ConfigureAwait(false)
                : await _controller.PrepareLocationAsync(request.State, request.Destination,
                    request.Options, request.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation and superseded requests do not change the existing panel.
        }
        catch (Exception error)
        {
            failure = error;
        }

        lock (_gate)
        {
            if (_stopping)
            {
                request.Release();
                return;
            }

            try
            {
                _postToUi(() => FinishOnUi(request, view, failure));
            }
            catch (Exception error)
            {
                ReportError(error);
                request.Release();
            }
        }
    }

    private void FinishOnUi(Pending request, PanelView? view, Exception? failure)
    {
        bool isCurrent;
        lock (_gate)
        {
            isCurrent = !_stopping && _pending.TryGetValue(request.State, out var current)
                && ReferenceEquals(current, request);
            if (isCurrent)
                _pending.Remove(request.State);
        }

        try
        {
            if (!isCurrent || request.Cancellation.IsCancellationRequested ||
                request.State.CurrentLocation != request.Origin ||
                request.State.ContentVersion != request.OriginVersion)
            {
                request.OnAbandoned?.Invoke();
                return;
            }
            if (failure is not null)
            {
                if (request.OnFailure is not null) request.OnFailure(failure);
                else ReportError(failure);
                return;
            }
            if (view is null)
            {
                request.OnAbandoned?.Invoke();
                return;
            }

            if (request.Refresh)
                _controller.CommitPreparedRefresh(request.State, view, request.VisibleRows);
            else
                _controller.CommitPreparedLocation(request.State, request.Destination, view);
            request.OnSuccess?.Invoke();
        }
        catch (Exception error)
        {
            ReportError(error);
        }
        finally
        {
            request.Release();
        }
    }

    /// <summary>Stop accepting completions before the source registry begins shutdown.</summary>
    public void Shutdown()
    {
        Pending[] active;
        lock (_gate)
        {
            _stopping = true;
            active = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (var request in active)
            RequestCancellation(request);
    }

    private void RequestCancellation(Pending request)
    {
        // A plugin can register a blocking cancellation callback; never execute
        // those callbacks on the terminal thread.
        _ = ObserveCancellationAsync(request.Cancellation.CancelAsync());
    }

    private async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        try { _reportError?.Invoke(error); }
        catch { /* Diagnostics must not disrupt panel loading. */ }
    }

    private sealed class Pending(
        FilePanelState state, PanelLocation origin, PanelLocation destination,
        AppSettings.PanelOptionsSettings options, bool refresh, int visibleRows,
        Action? onSuccess, Action<Exception>? onFailure, Action? onAbandoned, IDisposable lease)
    {
        public FilePanelState State { get; } = state;
        public PanelLocation Origin { get; } = origin;
        public long OriginVersion { get; } = state.ContentVersion;
        public PanelLocation Destination { get; } = destination;
        public AppSettings.PanelOptionsSettings Options { get; } = options;
        public bool Refresh { get; } = refresh;
        public int VisibleRows { get; } = visibleRows;
        public Action? OnSuccess { get; } = onSuccess;
        public Action<Exception>? OnFailure { get; } = onFailure;
        public Action? OnAbandoned { get; } = onAbandoned;
        public CancellationTokenSource Cancellation { get; } = new();
        private int _released;
        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lease.Dispose();
            Cancellation.Dispose();
        }
    }
}
