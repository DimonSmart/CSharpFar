namespace CSharpFar.App.Viewer;

internal sealed class MemoryFileByteReader : IFileByteReader
{
    private readonly byte[] _content;

    public MemoryFileByteReader(byte[] content)
    {
        _content = content ?? [];
    }

    public long Length => _content.LongLength;

    public Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (offset < 0 || offset >= _content.LongLength)
            return Task.FromResult(0);

        int available = (int)Math.Min(buffer.Length, _content.LongLength - offset);
        _content.AsMemory((int)offset, available).CopyTo(buffer);
        return Task.FromResult(available);
    }
}
