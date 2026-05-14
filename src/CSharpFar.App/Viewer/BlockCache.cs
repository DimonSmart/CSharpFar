namespace CSharpFar.App.Viewer;

internal sealed class BlockCache
{
    public const int DefaultBlockSize = 256 * 1024;
    public const int DefaultCapacity = 8;

    private readonly IFileByteReader _reader;
    private readonly int _capacity;
    private readonly Dictionary<long, CacheEntry> _entries = [];
    private long _lastAccess;

    public BlockCache(
        IFileByteReader reader,
        int blockSize = DefaultBlockSize,
        int capacity = DefaultCapacity)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _reader = reader;
        BlockSize = blockSize;
        _capacity = capacity;
    }

    public int BlockSize { get; }

    public async Task<ReadOnlyMemory<byte>> ReadBlockAsync(
        long offset,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        long blockStart = NormalizeOffset(offset);
        if (_entries.TryGetValue(blockStart, out var cached) && IsFresh(cached, blockStart))
        {
            cached.LastAccess = ++_lastAccess;
            return cached.Bytes;
        }

        var buffer = new byte[BlockSize];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await _reader
                .ReadAsync(blockStart + totalRead, buffer.AsMemory(totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            totalRead += read;
        }

        if (totalRead != buffer.Length)
            Array.Resize(ref buffer, totalRead);

        var entry = new CacheEntry(buffer, ++_lastAccess);
        _entries[blockStart] = entry;
        EvictIfNeeded();
        return entry.Bytes;
    }

    public async Task<int> ReadAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int totalRead = 0;
        while (totalRead < destination.Length && offset + totalRead < _reader.Length)
        {
            long currentOffset = offset + totalRead;
            ReadOnlyMemory<byte> block = await ReadBlockAsync(currentOffset, cancellationToken)
                .ConfigureAwait(false);
            if (block.IsEmpty)
                break;

            int blockOffset = (int)(currentOffset - NormalizeOffset(currentOffset));
            int available = block.Length - blockOffset;
            if (available <= 0)
                break;

            int toCopy = Math.Min(available, destination.Length - totalRead);
            block.Slice(blockOffset, toCopy).CopyTo(destination[totalRead..]);
            totalRead += toCopy;
        }

        return totalRead;
    }

    private long NormalizeOffset(long offset) => offset / BlockSize * BlockSize;

    private bool IsFresh(CacheEntry entry, long blockStart)
    {
        if (entry.Bytes.Length == BlockSize)
            return true;

        return blockStart + entry.Bytes.Length >= _reader.Length;
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > _capacity)
        {
            long oldestKey = _entries
                .OrderBy(pair => pair.Value.LastAccess)
                .First()
                .Key;
            _entries.Remove(oldestKey);
        }
    }

    private sealed class CacheEntry(byte[] bytes, long lastAccess)
    {
        public byte[] Bytes { get; } = bytes;
        public long LastAccess { get; set; } = lastAccess;
    }
}
