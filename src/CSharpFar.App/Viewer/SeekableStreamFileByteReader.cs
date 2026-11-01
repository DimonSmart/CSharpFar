namespace CSharpFar.App.Viewer;

/// <summary>Serialized random-access reads over an owned seekable provider stream.</summary>
internal sealed class SeekableStreamFileByteReader(Stream stream) : IFileByteReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public long Length => stream.Length;

    public async Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (buffer.IsEmpty || offset >= Length)
            return 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            stream.Seek(offset, SeekOrigin.Begin);
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
