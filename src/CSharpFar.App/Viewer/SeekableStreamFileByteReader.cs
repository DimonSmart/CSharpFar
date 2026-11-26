namespace CSharpFar.App.Viewer;

/// <summary>Serialized random-access reads over an owned seekable provider stream.</summary>
internal sealed class SeekableStreamFileByteReader(Stream stream, IDisposable? usage = null) : IFileByteReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly long _length = stream.Length;
    private int _activeReads;
    private int _disposed;
    private int _resourcesDisposed;
    private int _cancellationFinished;

    public long Length => _length;

    public async Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (buffer.IsEmpty || offset >= Length)
            return 0;

        Interlocked.Increment(ref _activeReads);
        if (Volatile.Read(ref _disposed) != 0)
        {
            ReleaseRead();
            throw new ObjectDisposedException(nameof(SeekableStreamFileByteReader));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                stream.Seek(offset, SeekOrigin.Begin);
                return await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            ReleaseRead();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _ = CancelAndDisposeAsync();
    }

    private async Task CancelAndDisposeAsync()
    {
        try { await _disposeCancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
        finally
        {
            Volatile.Write(ref _cancellationFinished, 1);
            TryDisposeResources();
        }
    }

    private void ReleaseRead()
    {
        Interlocked.Decrement(ref _activeReads);
        TryDisposeResources();
    }

    private void TryDisposeResources()
    {
        if (Volatile.Read(ref _disposed) == 0 ||
            Volatile.Read(ref _cancellationFinished) == 0 ||
            Volatile.Read(ref _activeReads) != 0 ||
            Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _gate.Dispose();
                _disposeCancellation.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
            finally { usage?.Dispose(); }
        });
    }
}
