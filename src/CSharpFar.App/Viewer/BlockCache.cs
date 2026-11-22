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

    public void Clear() => _entries.Clear();

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
            cancellationToken.ThrowIfCancellationRequested();
            long remaining = _reader.Length - (blockStart + totalRead);
            if (remaining <= 0)
                break;

            int requested = (int)Math.Min(buffer.Length - totalRead, remaining);
            int read = await _reader.ReadAsync(
                blockStart + totalRead, buffer.AsMemory(totalRead, requested), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            totalRead += read;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (totalRead != buffer.Length)
            Array.Resize(ref buffer, totalRead);

        // A provider might report a transient zero read before its advertised EOF.
        // Do not cache that incomplete interior block.
        if (blockStart + totalRead >= _reader.Length || totalRead == BlockSize)
        {
            var entry = new CacheEntry(buffer, ++_lastAccess);
            _entries[blockStart] = entry;
            EvictIfNeeded();
        }
        return buffer;
    }

    public async Task<int> ReadAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int totalRead = 0;
        while (totalRead < destination.Length)
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
        long length = _reader.Length;
        if (blockStart + entry.Bytes.Length > length)
            return false; // A previously cached block is stale after a shrink.
        return entry.Bytes.Length == BlockSize ||
            blockStart + entry.Bytes.Length >= length;
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
