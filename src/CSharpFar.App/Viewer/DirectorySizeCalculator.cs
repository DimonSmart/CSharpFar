using CSharpFar.Core.Abstractions;
using CSharpFar.FileSystem;

namespace CSharpFar.App.Viewer;

internal enum DirectoryScanProgressMode
{
    ReportProgress,
    Silent,
}

internal readonly record struct DirectoryScanUpdate(long OperationId, string Path, DirectorySizeState State);

internal interface IDirectorySizeCalculator : IDisposable
{
    event Action<DirectoryScanUpdate>? Progress;
    event Action<DirectoryScanUpdate>? Completed;

    long Start(string path, DirectoryScanProgressMode progressMode, Action<long>? operationStarted = null);
    void Cancel();
}

/// <summary>
/// Quick View-specific adapter over <see cref="DirectoryTreeSizeScanner"/>.
/// A new calculation cancels the previous one; the reusable scanner itself has no such policy.
/// Both events are raised on a thread-pool thread; callers must marshal to the UI themselves.
/// </summary>
internal sealed class DirectorySizeCalculator : IDirectorySizeCalculator
{
    public const int ThrottleMs = DirectoryTreeSizeScanner.DefaultThrottleMs;

    private readonly IFilePanelSource _source;
    private readonly IDirectoryTreeSizeScanner _scanner;
    private CancellationTokenSource _cts = new();
    private long _nextOperationId;

    public DirectorySizeCalculator(int throttleMs = ThrottleMs)
        : this(
            new LocalFilePanelSource(new FileSystemService()),
            new DirectoryTreeSizeScanner(throttleMs))
    {
    }

    internal DirectorySizeCalculator(
        IFilePanelSource source,
        IDirectoryTreeSizeScanner scanner)
    {
        _source = source;
        _scanner = scanner;
    }

    /// <summary>Intermediate progress update (throttled by the scanner).</summary>
    public event Action<DirectoryScanUpdate>? Progress;

    /// <summary>Final result when the scan is complete.</summary>
    public event Action<DirectoryScanUpdate>? Completed;

    public long Start(string path, DirectoryScanProgressMode progressMode, Action<long>? operationStarted = null)
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();

        var token = _cts.Token;
        long operationId = Interlocked.Increment(ref _nextOperationId);
        operationStarted?.Invoke(operationId);
        _ = Task.Run(() => Calculate(operationId, path, progressMode, token), token);
        return operationId;
    }

    public void Cancel()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    private void Calculate(
        long operationId,
        string path,
        DirectoryScanProgressMode progressMode,
        CancellationToken token)
    {
        try
        {
            Action<DirectoryTreeSizeProgress>? progress = progressMode == DirectoryScanProgressMode.ReportProgress
                ? value => Progress?.Invoke(new DirectoryScanUpdate(
                    operationId,
                    path,
                    new DirectorySizeState(value.Size, false, value.Errors)))
                : null;

            DirectoryTreeSizeScanResult result = _scanner.Scan(_source, path, progress, token);
            if (token.IsCancellationRequested)
                return;

            // Preserve the existing Quick View contract: completion is a final snapshot,
            // and traversal errors are carried in DirectorySizeState.Errors.
            Completed?.Invoke(new DirectoryScanUpdate(
                operationId,
                path,
                new DirectorySizeState(result.Size, true, result.Errors)));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
