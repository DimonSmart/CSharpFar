namespace CSharpFar.App.Viewer;

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

    /// <summary>Intermediate progress update (throttled).</summary>
    public event Action<string, DirectorySizeState>? Progress;

    /// <summary>Final result when the scan is complete.</summary>
    public event Action<string, DirectorySizeState>? Completed;

    private CancellationTokenSource _cts = new();

    public void Start(string path)
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();

        var token = _cts.Token;
        Task.Run(() => Calculate(path, token), token);
    }

    public void Cancel()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    private void Calculate(string path, CancellationToken token)
    {
        try
        {
            long total = 0;
            var errors = new List<string>();
            var stack  = new Stack<string>();
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
                catch (IOException ex)               { errors.Add($"{dir}: {ex.Message}"); }

                // Throttled progress
                long now = Environment.TickCount64;
                if (now - lastProgressTick >= ThrottleMs)
                {
                    lastProgressTick = now;
                    var state = new DirectorySizeState(total, false, [.. errors]);
                    Progress?.Invoke(path, state);
                }
            }

            if (!token.IsCancellationRequested)
            {
                var final = new DirectorySizeState(total, true, [.. errors]);
                Completed?.Invoke(path, final);
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
