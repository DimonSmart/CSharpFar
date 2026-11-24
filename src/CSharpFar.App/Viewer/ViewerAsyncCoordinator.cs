namespace CSharpFar.App.Viewer;

internal sealed class ViewerAsyncCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _latest = [];
    private readonly HashSet<PrefetchKey> _prefetches = [];
    private readonly Queue<Action> _uiCompletions = [];
    private CancellationTokenSource _session = new();
    private long _sessionVersion;
    private long _stateVersion;
    private int _pending;
    private bool _disposed;

    public long StateVersion
    {
        get
        {
            lock (_gate)
                return _stateVersion;
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
                return _pending > 0 || _uiCompletions.Count > 0;
        }
    }

    public bool IsChannelPending(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        lock (_gate)
            return _latest.ContainsKey(channel);
    }



    public void BeginSession()
    {
        CancellationTokenSource oldSession;
        CancellationTokenSource[] latest;
        lock (_gate)
        {
            ThrowIfDisposed();
            oldSession = _session;
            _session = new CancellationTokenSource();
            _sessionVersion++;
            _stateVersion++;
            latest = _latest.Values.ToArray();
            _latest.Clear();
            _prefetches.Clear();
            _uiCompletions.Clear();
        }

        oldSession.Cancel();
        oldSession.Dispose();
        foreach (CancellationTokenSource cancellation in latest)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    public long InvalidateState()
    {
        CancellationTokenSource[] latest;
        lock (_gate)
        {
            ThrowIfDisposed();
            _stateVersion++;
            latest = _latest.Values.ToArray();
            _latest.Clear();
        }

        foreach (CancellationTokenSource cancellation in latest)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        return StateVersion;
    }

    public void Prefetch(BlockCache cache, long offset)
    {
        ArgumentNullException.ThrowIfNull(cache);
        long blockOffset = cache.GetBlockStart(offset);
        PrefetchKey key = new(cache, blockOffset);
        long sessionVersion;
        CancellationToken sessionToken;

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_prefetches.Add(key))
                return;

            sessionVersion = _sessionVersion;
            sessionToken = _session.Token;
            _pending++;
        }

        _ = RunPrefetchAsync(key, sessionVersion, sessionToken);
    }

    public void StartLatest<T>(
        string channel,
        long expectedStateVersion,
        Func<CancellationToken, Task<T>> operation,
        Action<T> apply,
        Action<Exception>? handleError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(apply);

        CancellationTokenSource? previous = null;
        CancellationTokenSource cancellation;
        long sessionVersion;

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_latest.Remove(channel, out previous))
            {
                // Cancellation/disposal happens outside the lock.
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
            _latest[channel] = cancellation;
            sessionVersion = _sessionVersion;
            _pending++;
        }

        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = RunLatestAsync(
            channel,
            sessionVersion,
            expectedStateVersion,
            cancellation,
            operation,
            apply,
            handleError);
    }

    public bool CancelLatest(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_latest.Remove(channel, out cancellation))
                return false;
        }

        cancellation.Cancel();
        cancellation.Dispose();
        return true;
    }

    public bool DrainUiCompletions()
    {
        Action[] completions;
        lock (_gate)
        {
            if (_uiCompletions.Count == 0)
                return false;

            completions = _uiCompletions.ToArray();
            _uiCompletions.Clear();
        }

        foreach (Action completion in completions)
            completion();

        return true;
    }

    public void Dispose()
    {
        CancellationTokenSource session;
        CancellationTokenSource[] latest;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            session = _session;
            latest = _latest.Values.ToArray();
            _latest.Clear();
            _prefetches.Clear();
            _uiCompletions.Clear();
        }

        session.Cancel();
        session.Dispose();
        foreach (CancellationTokenSource cancellation in latest)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private async Task RunPrefetchAsync(
        PrefetchKey key,
        long sessionVersion,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            await Task.Yield();
            await key.Cache.ReadBlockAsync(key.BlockOffset, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            lock (_gate)
            {
                _pending = Math.Max(0, _pending - 1);
                _prefetches.Remove(key);
                if (!_disposed && sessionVersion == _sessionVersion)
                {
                    // A no-op completion is enough to make the UI pump redraw.
                    // Read failures are deliberately consumed here; the next actual
                    // viewer operation will surface a persistent provider error.
                    _uiCompletions.Enqueue(static () => { });
                }
            }
        }

        _ = error;
    }

    private async Task RunLatestAsync<T>(
        string channel,
        long sessionVersion,
        long expectedStateVersion,
        CancellationTokenSource cancellation,
        Func<CancellationToken, Task<T>> operation,
        Action<T> apply,
        Action<Exception>? handleError)
    {
        T? result = default;
        Exception? error = null;
        bool completed = false;
        try
        {
            result = await operation(cancellation.Token).ConfigureAwait(false);
            completed = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }

        lock (_gate)
        {
            _pending = Math.Max(0, _pending - 1);

            bool isCurrent =
                !_disposed &&
                sessionVersion == _sessionVersion &&
                expectedStateVersion == _stateVersion &&
                _latest.TryGetValue(channel, out CancellationTokenSource? current) &&
                ReferenceEquals(current, cancellation);

            if (isCurrent)
            {
                _latest.Remove(channel);
                if (completed)
                {
                    _uiCompletions.Enqueue(() =>
                    {
                        if (IsCurrentVersion(sessionVersion, expectedStateVersion))
                            apply(result!);
                    });
                }
                else if (error is not null && handleError is not null)
                {
                    _uiCompletions.Enqueue(() =>
                    {
                        if (IsCurrentVersion(sessionVersion, expectedStateVersion))
                            handleError(error);
                    });
                }
            }
        }

        cancellation.Dispose();
    }

    private bool IsCurrentVersion(long sessionVersion, long stateVersion)
    {
        lock (_gate)
        {
            return !_disposed &&
                   _sessionVersion == sessionVersion &&
                   _stateVersion == stateVersion;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ViewerAsyncCoordinator));
    }

    private readonly record struct PrefetchKey(BlockCache Cache, long BlockOffset);
}
