namespace CSharpFar.App.Viewer;

/// <summary>Serialized random-access reads over an owned seekable provider stream.</summary>
internal sealed class SeekableStreamFileByteReader(Stream stream) : IFileByteReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _activeReads;
    private int _disposed;

    public long Length => stream.Length;

    public async Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (buffer.IsEmpty || offset >= Length)
            return 0;

        Interlocked.Increment(ref _activeReads);
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
            if (Interlocked.Decrement(ref _activeReads) == 0 &&
                Volatile.Read(ref _disposed) != 0)
            {
                _gate.Dispose();
                _disposeCancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCancellation.Cancel();
        if (Volatile.Read(ref _activeReads) == 0)
        {
            _gate.Dispose();
            _disposeCancellation.Dispose();
        }
    }
}
