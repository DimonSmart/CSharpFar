using CSharpFar.App.Viewer;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.App.Panels;

internal enum PanelDirectorySizeState
{
    Pending,
    Calculating,
    Completed,
    CompletedWithErrors,
    Failed,
}

internal readonly record struct PanelDirectorySizePresentation(
    long? DisplaySize,
    bool IsInProgress);

internal sealed record PanelDirectorySizeEntrySnapshot(
    PanelDirectorySizeState State,
    long? LastCompletedSize,
    long? CurrentPartialSize,
    long OperationId,
    IReadOnlyList<string> Errors);

/// <summary>
/// Owns the ephemeral directory-size state of the left and right panels.
/// Traversal is delegated to <see cref="IDirectoryTreeSizeScanner"/> and always runs on a bounded background worker.
/// </summary>
internal sealed class PanelDirectorySizeCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly FilePanelSourceRegistry _sources;
    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly Action _wakeInputLoop;
    private readonly IDirectoryTreeSizeScanner _scanner;
    private readonly Dictionary<PanelSide, PanelDirectorySizeSession> _sessions = new();
    private readonly Dictionary<PanelSourceId, SemaphoreSlim> _providerGates = new();
    private long _nextSessionId;
    private long _nextOperationId;
    private bool _disposed;

    public PanelDirectorySizeCoordinator(
        FilePanelSourceRegistry sources,
        FilePanelState left,
        FilePanelState right,
        Action wakeInputLoop,
        IDirectoryTreeSizeScanner? scanner = null)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _wakeInputLoop = wakeInputLoop ?? throw new ArgumentNullException(nameof(wakeInputLoop));
        _scanner = scanner ?? new DirectoryTreeSizeScanner();
    }

    public bool CanCalculate(FilePanelState state, FilePanelItem? item)
    {
        if (item is null ||
            state.ContentKind != PanelContentKind.Source ||
            !DirectoryTraversalPolicy.IsEligibleDirectory(item) ||
            item.SourceId != state.SourceId ||
            !HasCapability(state.ProviderCapabilities, PanelProviderCapabilities.Enumerate) ||
            !_sources.TryGetSource(state.SourceId, out IFilePanelSource source) ||
            !HasCapability(source.Capabilities, PanelProviderCapabilities.Enumerate))
        {
            return false;
        }

        try
        {
            _ = source.NormalizePath(item.SourcePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool CanCalculateAll(FilePanelState state) =>
        state.ContentKind == PanelContentKind.Source &&
        HasCapability(state.ProviderCapabilities, PanelProviderCapabilities.Enumerate) &&
        _sources.TryGetSource(state.SourceId, out IFilePanelSource source) &&
        HasCapability(source.Capabilities, PanelProviderCapabilities.Enumerate) &&
        state.Items.Any(item => CanCalculate(state, item));

    public bool Calculate(PanelSide side, FilePanelState state, FilePanelItem item)
    {
        if (!CanCalculate(state, item))
            return false;

        bool started;
        lock (_gate)
        {
            if (_disposed || !TryEnsureSessionNoLock(side, state, out PanelDirectorySizeSession? session))
                return false;

            ReconcileNoLock(session, state);
            started = QueueOperationNoLock(session, item.SourcePath);
        }

        if (started)
            _wakeInputLoop();
        return started;
    }

    public int CalculateAll(PanelSide side, FilePanelState state)
    {
        if (!CanCalculateAll(state))
            return 0;

        int started = 0;
        lock (_gate)
        {
            if (_disposed || !TryEnsureSessionNoLock(side, state, out PanelDirectorySizeSession? session))
                return 0;

            ReconcileNoLock(session, state);
            foreach (FilePanelItem item in state.Items)
            {
                if (CanCalculate(state, item) && QueueOperationNoLock(session, item.SourcePath))
                    started++;
            }
        }

        if (started > 0)
            _wakeInputLoop();
        return started;
    }

    public void Reconcile(PanelSide side, FilePanelState state)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_disposed)
                return;

            if (state.ContentKind != PanelContentKind.Source)
            {
                changed = InvalidateSideNoLock(side);
            }
            else if (_sessions.TryGetValue(side, out PanelDirectorySizeSession? session))
            {
                changed = !SessionMatchesStateNoLock(session, state)
                    ? InvalidateSideNoLock(side)
                    : ReconcileNoLock(session, state);
            }
        }

        if (changed)
            _wakeInputLoop();
    }

    public PanelDirectorySizePresentation? GetPresentation(
        PanelSide side,
        FilePanelState state,
        FilePanelItem item)
    {
        if (state.ContentKind != PanelContentKind.Source || !DirectoryTraversalPolicy.IsEligibleDirectory(item))
            return null;

        lock (_gate)
        {
            if (_disposed ||
                !_sessions.TryGetValue(side, out PanelDirectorySizeSession? session) ||
                !SessionMatchesStateNoLock(session, state))
            {
                return null;
            }

            string path;
            try { path = session.Source.NormalizePath(item.SourcePath); }
            catch { return null; }

            if (!session.Entries.TryGetValue(path, out Entry? entry))
                return null;

            return entry.State switch
            {
                PanelDirectorySizeState.Pending or PanelDirectorySizeState.Calculating
                    when entry.LastCompletedSize is { } last => new(last, true),
                PanelDirectorySizeState.Calculating
                    when entry.CurrentPartialSize is { } partial => new(partial, true),
                PanelDirectorySizeState.Pending => new(null, true),
                PanelDirectorySizeState.Completed or PanelDirectorySizeState.CompletedWithErrors
                    when entry.LastCompletedSize is { } completed => new(completed, false),
                PanelDirectorySizeState.Failed
                    when entry.LastCompletedSize is { } previous => new(previous, false),
                PanelDirectorySizeState.Failed => new(null, false),
                _ => null,
            };
        }
    }

    internal PanelDirectorySizeEntrySnapshot? GetSnapshot(
        PanelSide side,
        FilePanelState state,
        FilePanelItem item)
    {
        lock (_gate)
        {
            if (_disposed ||
                !_sessions.TryGetValue(side, out PanelDirectorySizeSession? session) ||
                !SessionMatchesStateNoLock(session, state))
            {
                return null;
            }

            string path;
            try { path = session.Source.NormalizePath(item.SourcePath); }
            catch { return null; }

            return session.Entries.TryGetValue(path, out Entry? entry)
                ? new PanelDirectorySizeEntrySnapshot(
                    entry.State,
                    entry.LastCompletedSize,
                    entry.CurrentPartialSize,
                    entry.OperationId,
                    entry.Errors)
                : null;
        }
    }

    private bool QueueOperationNoLock(PanelDirectorySizeSession session, string sourcePath)
    {
        string path;
        try { path = session.Source.NormalizePath(sourcePath); }
        catch { return false; }

        if (session.Entries.TryGetValue(path, out Entry? existing) &&
            existing.State is PanelDirectorySizeState.Pending or PanelDirectorySizeState.Calculating)
        {
            return false;
        }

        Entry entry = existing ?? new Entry(path);
        entry.OperationCancellation?.Cancel();
        entry.OperationCancellation?.Dispose();
        entry.OperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token);
        entry.OperationId = Interlocked.Increment(ref _nextOperationId);
        entry.CurrentPartialSize = null;
        entry.Errors = [];
        entry.State = PanelDirectorySizeState.Pending;
        session.Entries[path] = entry;
        session.Queue.Enqueue(new QueuedOperation(path, entry.OperationId));
        EnsureWorkerNoLock(session);
        return true;
    }

    private void EnsureWorkerNoLock(PanelDirectorySizeSession session)
    {
        if (session.WorkerRunning || session.Cancellation.IsCancellationRequested)
            return;

        session.WorkerRunning = true;
        _ = Task.Run(() => ProcessQueueAsync(session));
    }

    private async Task ProcessQueueAsync(PanelDirectorySizeSession session)
    {
        while (true)
        {
            QueuedOperation operation;
            CancellationToken operationToken;

            lock (_gate)
            {
                if (_disposed || session.Cancellation.IsCancellationRequested)
                {
                    session.WorkerRunning = false;
                    return;
                }

                if (!IsCurrentSessionNoLock(session))
                {
                    InvalidateRegisteredSessionNoLock(session);
                    session.WorkerRunning = false;
                    return;
                }

                if (!TryTakeQueuedOperationNoLock(session, out operation, out operationToken))
                {
                    session.WorkerRunning = false;
                    return;
                }
            }

            SemaphoreSlim providerGate = GetProviderGate(session.Source.SourceId);
            bool gateEntered = false;
            try
            {
                await providerGate.WaitAsync(operationToken).ConfigureAwait(false);
                gateEntered = true;

                lock (_gate)
                {
                    if (!TryGetCurrentOperationNoLock(session, operation, out Entry? entry))
                    {
                        InvalidateRegisteredSessionNoLock(session);
                        continue;
                    }
                    entry.State = PanelDirectorySizeState.Calculating;
                }
                _wakeInputLoop();

                DirectoryTreeSizeScanResult result = _scanner.Scan(
                    session.Source,
                    operation.Path,
                    progress => OnProgress(session, operation, progress),
                    operationToken);
                OnCompleted(session, operation, result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnCompleted(
                    session,
                    operation,
                    new DirectoryTreeSizeScanResult(
                        0,
                        DirectoryTreeSizeCompletionStatus.Failed,
                        [$"{operation.Path}: {ex.Message}"]));
            }
            finally
            {
                if (gateEntered)
                    providerGate.Release();
            }
        }
    }

    private bool TryTakeQueuedOperationNoLock(
        PanelDirectorySizeSession session,
        out QueuedOperation operation,
        out CancellationToken token)
    {
        while (session.Queue.Count > 0)
        {
            operation = session.Queue.Dequeue();
            if (!session.Entries.TryGetValue(operation.Path, out Entry? entry) ||
                entry.OperationId != operation.OperationId ||
                entry.State != PanelDirectorySizeState.Pending ||
                entry.OperationCancellation is null)
            {
                continue;
            }

            token = entry.OperationCancellation.Token;
            return true;
        }

        operation = default;
        token = default;
        return false;
    }

    private void OnProgress(
        PanelDirectorySizeSession session,
        QueuedOperation operation,
        DirectoryTreeSizeProgress progress)
    {
        bool accepted = false;
        lock (_gate)
        {
            if (TryGetCurrentOperationNoLock(session, operation, out Entry? entry) &&
                entry.State == PanelDirectorySizeState.Calculating)
            {
                entry.CurrentPartialSize = progress.Size;
                entry.Errors = progress.Errors;
                accepted = true;
            }
            else
            {
                InvalidateRegisteredSessionNoLock(session);
            }
        }

        if (accepted)
            _wakeInputLoop();
    }

    private void OnCompleted(
        PanelDirectorySizeSession session,
        QueuedOperation operation,
        DirectoryTreeSizeScanResult result)
    {
        bool accepted = false;
        lock (_gate)
        {
            if (!TryGetCurrentOperationNoLock(session, operation, out Entry? entry))
            {
                InvalidateRegisteredSessionNoLock(session);
                return;
            }

            entry.Errors = result.Errors;
            entry.CurrentPartialSize = null;
            entry.OperationCancellation?.Dispose();
            entry.OperationCancellation = null;

            switch (result.Status)
            {
                case DirectoryTreeSizeCompletionStatus.Completed:
                    entry.LastCompletedSize = result.Size;
                    entry.State = PanelDirectorySizeState.Completed;
                    break;
                case DirectoryTreeSizeCompletionStatus.CompletedWithErrors:
                    entry.LastCompletedSize = result.Size;
                    entry.State = PanelDirectorySizeState.CompletedWithErrors;
                    break;
                case DirectoryTreeSizeCompletionStatus.Failed:
                    entry.State = PanelDirectorySizeState.Failed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            accepted = true;
        }

        if (accepted)
            _wakeInputLoop();
    }

    private bool TryGetCurrentOperationNoLock(
        PanelDirectorySizeSession session,
        QueuedOperation operation,
        out Entry entry)
    {
        entry = null!;
        return IsCurrentSessionNoLock(session) &&
               !session.Cancellation.IsCancellationRequested &&
               session.Entries.TryGetValue(operation.Path, out entry!) &&
               entry.OperationId == operation.OperationId;
    }

    private bool TryEnsureSessionNoLock(
        PanelSide side,
        FilePanelState state,
        out PanelDirectorySizeSession session)
    {
        session = null!;
        if (state.ContentKind != PanelContentKind.Source ||
            !_sources.TryGetSource(state.SourceId, out IFilePanelSource source) ||
            !HasCapability(source.Capabilities, PanelProviderCapabilities.Enumerate))
        {
            InvalidateSideNoLock(side);
            return false;
        }

        if (_sessions.TryGetValue(side, out PanelDirectorySizeSession? existing))
        {
            if (SessionMatchesStateNoLock(existing, state))
            {
                session = existing;
                return true;
            }

            InvalidateSideNoLock(side);
        }

        string normalizedLocation;
        try { normalizedLocation = source.NormalizePath(state.SourcePath); }
        catch { return false; }

        session = new PanelDirectorySizeSession(
            Interlocked.Increment(ref _nextSessionId),
            side,
            new PanelLocation(state.SourceId, normalizedLocation),
            source,
            PathComparer(state.SourceId));
        _sessions[side] = session;
        return true;
    }

    private bool ReconcileNoLock(PanelDirectorySizeSession session, FilePanelState state)
    {
        var present = new HashSet<string>(session.PathComparer);
        foreach (FilePanelItem item in state.Items)
        {
            if (!CanCalculate(state, item))
                continue;

            try { present.Add(session.Source.NormalizePath(item.SourcePath)); }
            catch { }
        }

        string[] removed = session.Entries.Keys.Where(path => !present.Contains(path)).ToArray();
        if (removed.Length == 0)
            return false;

        var removedSet = new HashSet<string>(removed, session.PathComparer);
        foreach (string path in removed)
        {
            if (!session.Entries.Remove(path, out Entry? entry))
                continue;
            entry.OperationCancellation?.Cancel();
            entry.OperationCancellation?.Dispose();
        }

        if (session.Queue.Count > 0)
        {
            QueuedOperation[] keep = session.Queue
                .Where(operation => !removedSet.Contains(operation.Path))
                .ToArray();
            session.Queue.Clear();
            foreach (QueuedOperation operation in keep)
                session.Queue.Enqueue(operation);
        }

        return true;
    }

    private bool SessionMatchesStateNoLock(PanelDirectorySizeSession session, FilePanelState state) =>
        state.ContentKind == PanelContentKind.Source &&
        LocationsEqual(session.Source, session.Location, state.CurrentLocation);

    private bool IsCurrentSessionNoLock(PanelDirectorySizeSession session) =>
        _sessions.TryGetValue(session.Side, out PanelDirectorySizeSession? current) &&
        ReferenceEquals(current, session) &&
        current.SessionId == session.SessionId &&
        SessionMatchesStateNoLock(session, StateFor(session.Side));

    private FilePanelState StateFor(PanelSide side) =>
        side == PanelSide.Left ? _left : _right;

    private static bool LocationsEqual(
        IFilePanelSource source,
        PanelLocation expected,
        PanelLocation actual)
    {
        if (expected.SourceId != actual.SourceId)
            return false;

        try
        {
            string normalized = source.NormalizePath(actual.SourcePath);
            return PathComparer(actual.SourceId).Equals(expected.SourcePath, normalized);
        }
        catch
        {
            return false;
        }
    }

    private void InvalidateRegisteredSessionNoLock(PanelDirectorySizeSession session)
    {
        if (_sessions.TryGetValue(session.Side, out PanelDirectorySizeSession? registered) &&
            ReferenceEquals(registered, session) &&
            !SessionMatchesStateNoLock(session, StateFor(session.Side)))
        {
            InvalidateSideNoLock(session.Side);
        }
    }

    private bool InvalidateSideNoLock(PanelSide side)
    {
        if (!_sessions.Remove(side, out PanelDirectorySizeSession? session))
            return false;

        session.Cancellation.Cancel();
        session.Queue.Clear();
        foreach (Entry entry in session.Entries.Values)
        {
            entry.OperationCancellation?.Cancel();
            entry.OperationCancellation?.Dispose();
        }
        session.Entries.Clear();
        return true;
    }

    private SemaphoreSlim GetProviderGate(PanelSourceId sourceId)
    {
        lock (_gate)
        {
            if (!_providerGates.TryGetValue(sourceId, out SemaphoreSlim? providerGate))
            {
                providerGate = new SemaphoreSlim(1, 1);
                _providerGates[sourceId] = providerGate;
            }
            return providerGate;
        }
    }

    private static bool HasCapability(
        PanelProviderCapabilities capabilities,
        PanelProviderCapabilities capability) =>
        (capabilities & capability) == capability;

    private static StringComparer PathComparer(PanelSourceId sourceId) =>
        sourceId == PanelSourceId.Local
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            InvalidateSideNoLock(PanelSide.Left);
            InvalidateSideNoLock(PanelSide.Right);
            _providerGates.Clear();
        }
    }

    private sealed class PanelDirectorySizeSession
    {
        public PanelDirectorySizeSession(
            long sessionId,
            PanelSide side,
            PanelLocation location,
            IFilePanelSource source,
            StringComparer pathComparer)
        {
            SessionId = sessionId;
            Side = side;
            Location = location;
            Source = source;
            PathComparer = pathComparer;
            Entries = new Dictionary<string, Entry>(pathComparer);
        }

        public long SessionId { get; }
        public PanelSide Side { get; }
        public PanelLocation Location { get; }
        public IFilePanelSource Source { get; }
        public StringComparer PathComparer { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Dictionary<string, Entry> Entries { get; }
        public Queue<QueuedOperation> Queue { get; } = new();
        public bool WorkerRunning { get; set; }
    }

    private sealed class Entry
    {
        public Entry(string path) => Path = path;

        public string Path { get; }
        public PanelDirectorySizeState State { get; set; }
        public long? LastCompletedSize { get; set; }
        public long? CurrentPartialSize { get; set; }
        public long OperationId { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = [];
        public CancellationTokenSource? OperationCancellation { get; set; }
    }

    private readonly record struct QueuedOperation(string Path, long OperationId);
}
