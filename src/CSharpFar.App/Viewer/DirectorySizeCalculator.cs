namespace CSharpFar.App.Viewer;

internal enum DirectoryScanProgressMode
{
    ReportProgress,
    Silent,
}

internal readonly record struct DirectoryScanUpdate(long OperationId, string Path, DirectorySizeState State);

/// <summary>
/// Calculates total size of a directory tree asynchronously.
/// A new calculation cancels the previous one.
/// <para>
/// <see cref="Progress"/> fires at most once per <see cref="ThrottleMs"/> milliseconds with
/// intermediate results so the UI can show live progress.
/// <see cref="Completed"/> fires once with the final result (including all errors).
/// Both events are raised on a thread-pool thread; callers must marshal to the UI themselves.
/// </para>
/// </summary>
internal sealed class DirectorySizeCalculator : IDisposable
{
    public const int ThrottleMs = 300;
    private readonly int _throttleMs;

    /// <summary>Intermediate progress update (throttled).</summary>
    public event Action<DirectoryScanUpdate>? Progress;

    /// <summary>Final result when the scan is complete.</summary>
    public event Action<DirectoryScanUpdate>? Completed;

    private CancellationTokenSource _cts = new();
    private long _nextOperationId;

    internal DirectorySizeCalculator(int throttleMs = ThrottleMs) => _throttleMs = throttleMs;

    public long Start(string path, DirectoryScanProgressMode progressMode, Action<long>? operationStarted = null)
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();

        var token = _cts.Token;
        long operationId = Interlocked.Increment(ref _nextOperationId);
        operationStarted?.Invoke(operationId);
        Task.Run(() => Calculate(operationId, path, progressMode, token), token);
        return operationId;
    }

    public void Cancel()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    private void Calculate(long operationId, string path, DirectoryScanProgressMode progressMode, CancellationToken token)
    {
        try
        {
            long total = 0;
            var errors = new List<string>();
            var stack = new Stack<string>();
            stack.Push(path);

            long lastProgressTick = Environment.TickCount64;

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();

                string dir = stack.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try { total += new FileInfo(file).Length; }
                        catch (Exception ex) { errors.Add($"{file}: {ex.Message}"); }
                    }

                    foreach (string sub in Directory.GetDirectories(dir))
                        stack.Push(sub);
                }
                catch (UnauthorizedAccessException ex) { errors.Add($"{dir}: {ex.Message}"); }
                catch (IOException ex) { errors.Add($"{dir}: {ex.Message}"); }

                // Throttled progress
                long now = Environment.TickCount64;
                if (progressMode == DirectoryScanProgressMode.ReportProgress && now - lastProgressTick >= _throttleMs)
                {
                    lastProgressTick = now;
                    var state = new DirectorySizeState(total, false, [.. errors]);
                    Progress?.Invoke(new DirectoryScanUpdate(operationId, path, state));
                }
            }

            if (!token.IsCancellationRequested)
            {
                var final = new DirectorySizeState(total, true, [.. errors]);
                Completed?.Invoke(new DirectoryScanUpdate(operationId, path, final));
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
