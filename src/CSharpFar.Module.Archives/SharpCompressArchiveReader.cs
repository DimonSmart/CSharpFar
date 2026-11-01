using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CSharpFar.Module.Archives;

internal static class SharpCompressArchiveReader
{
    private static bool CompressedTar(string name) =>
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    public static (string Format, ArchiveEntryIndex Index) BuildIndex(
        string file, string name, CancellationToken cancellationToken)
    {
        ArchiveEntryIndex index = new();
        if (CompressedTar(name))
        {
            using Stream stream = File.OpenRead(file);
            using IReader reader = ReaderFactory.OpenReader(stream);
            if (reader.Type != ArchiveType.Tar)
                throw new InvalidDataException("The file does not contain a GZip-compressed TAR archive.");
            int ordinal = 0;
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add(index, reader.Entry, ordinal++);
            }
            return ("TAR.GZ", index);
        }

        using var input = File.OpenRead(file);
        using var archive = ArchiveFactory.OpenArchive(input);
        string format = archive.Type switch
        {
            ArchiveType.Zip => "ZIP",
            ArchiveType.Rar => "RAR",
            ArchiveType.SevenZip => "7Z",
            ArchiveType.Tar => "TAR",
            _ => throw new NotSupportedException($"Unsupported archive type: {archive.Type}"),
        };

        int entryNumber = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsComplete)
                throw new NotSupportedException("Incomplete or multi-volume archive entries are unsupported.");
            Add(index, entry, entryNumber++);
        }
        return (format, index);
    }

    private static void Add(ArchiveEntryIndex index, IEntry entry, int ordinal)
    {
        // An explicit TAR root entry is not a child of the virtual root.
        if (entry.IsDirectory && entry.Key is "." or "./")
            return;
        index.Add(entry, ordinal);
    }

    public static Stream OpenEntry(
        string file, string format, ArchiveNode node, CancellationToken cancellationToken)
    {
        Stream input = File.OpenRead(file);
        IArchive? archive = null;
        IReader? reader = null;
        try
        {
            if (format is "TAR.GZ" or "TAR")
            {
                reader = ReaderFactory.OpenReader(input);
                return FindSequential(reader, input, null, node, cancellationToken);
            }

            archive = ArchiveFactory.OpenArchive(input);
            if (archive.IsSolid)
            {
                reader = archive.ExtractAllEntries();
                return FindSequential(reader, input, archive, node, cancellationToken);
            }

            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.Key is not null &&
                ArchivePath.NormalizeEntry(candidate.Key) == node.Path);
            if (entry is null)
                throw new IOException("Archive entry disappeared.");
            if (!entry.IsComplete || entry.IsEncrypted)
                throw new NotSupportedException("Incomplete or encrypted archive entry.");

            Stream stream = entry.OpenEntryStream();
            return new BoundedArchiveStream(stream, input, archive, null, node.Size);
        }
        catch
        {
            reader?.Dispose();
            archive?.Dispose();
            input.Dispose();
            throw;
        }
    }

    private static Stream FindSequential(
        IReader reader, Stream input, IArchive? archive, ArchiveNode node,
        CancellationToken cancellationToken)
    {
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEntry entry = reader.Entry;
            if (entry.Key is null || entry.IsDirectory)
                continue;
            if (ArchivePath.NormalizeEntry(entry.Key) != node.Path)
                continue;
            if (entry.IsEncrypted || entry.IsSplitAfter)
                throw new NotSupportedException("Incomplete or encrypted archive entry.");

            return new BoundedArchiveStream(
                reader.OpenEntryStream(), input, archive, reader, node.Size);
        }
        throw new IOException("Archive entry disappeared.");
    }
}

/// <summary>Releases the decoder, archive and compressed input when the viewer or copy operation finishes.</summary>
internal sealed class BoundedArchiveStream(
    Stream decompressed, Stream input, IArchive? archive, IReader? reader, long expectedSize) : Stream
{
    private long _read;
    private bool _disposed;

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private void Account(int count)
    {
        _read = checked(_read + count);
        if (_read > ArchiveEntryIndex.MaxSingleSize ||
            _read > expectedSize + Math.Min(expectedSize, 1024L * 1024))
            throw new InvalidDataException("Archive entry expanded beyond its declared size.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = decompressed.Read(buffer, offset, count);
        Account(n);
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        int n = decompressed.Read(buffer);
        Account(n);
        return n;
    }

    // Decoder implementations (notably LZMA) may not support asynchronous
    // decompression reliably. Read on a worker rather than using decoder ReadAsync.
    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Read(buffer, offset, count);
        }, cancellationToken);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Read(buffer.Span);
        }, cancellationToken));

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try { decompressed.Dispose(); }
            finally
            {
                try { reader?.Dispose(); }
                finally
                {
                    try { archive?.Dispose(); }
                    finally { input.Dispose(); }
                }
            }
        }
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
