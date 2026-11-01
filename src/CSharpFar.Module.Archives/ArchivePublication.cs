using System.IO.Compression;
using System.Security.Cryptography;

namespace CSharpFar.Module.Archives;

/// <summary>
/// Transactional ZIP rebuild. All output is written beside the original and
/// validated before File.Replace; the original is never opened for writing.
/// A sidecar file serializes cooperative writers across CSharpFar processes.
/// </summary>
internal static class ArchivePublication
{
    private const int BufferSize = 65536;

    public static async Task PublishAsync(
        ArchiveFilePanelSource owner, ArchiveMutationBatch batch, CancellationToken cancellationToken)
    {
        owner.CheckFresh();
        string original = owner.ArchiveFile;
        string directory = Path.GetDirectoryName(original)!;
        string temporary = Path.Combine(directory,
            "." + Path.GetFileName(original) + ".csharpfar-" + Guid.NewGuid().ToString("N") + ".tmp");
        string backup = temporary + ".backup";
        bool published = false;
        try
        {
            // FileShare.None is the cross-process cooperative lock; the lock file
            // is intentionally reusable (deleting it would introduce a lock race).
            using var guard = new FileStream(original + ".csharpfar-lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await CheckRevisionAsync(owner, batch.OriginalDigest, cancellationToken).ConfigureAwait(false);
            ArchiveZipSafety.Validate(original);
            await WriteZipAsync(owner, batch, temporary, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var (format, validatedIndex) = await Task.Run(() =>
                SharpCompressArchiveReader.BuildIndex(temporary, original, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (format != owner.Format)
                throw new InvalidDataException("Archive format changed during rebuild.");
            ValidateIndex(batch, validatedIndex);
            await VerifyChangedFilesAsync(temporary, batch, cancellationToken).ConfigureAwait(false);

            // Re-check after the potentially lengthy compression and verification.
            // External non-cooperating writers can still race the final OS replace.
            await CheckRevisionAsync(owner, batch.OriginalDigest, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // File.Replace is a single filesystem replacement, never delete+move.
            // If unavailable, reject rather than putting the original at risk.
            File.Replace(temporary, original, backup, ignoreMetadataErrors: false);
            published = true;

            // Publication is already committed; cleanup/refresh must not turn a
            // successful write into a reported abort.
            try { owner.Published(validatedIndex); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally
        {
            if (!published)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            else
            {
                try { File.Delete(backup); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task CheckRevisionAsync(
        ArchiveFilePanelSource owner, byte[] expectedDigest, CancellationToken cancellationToken)
    {
        owner.CheckFresh();
        using var stream = new FileStream(owner.ArchiveFile, FileMode.Open,
            FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
        byte[] current = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!current.AsSpan().SequenceEqual(expectedDigest))
            throw new IOException("Archive contents changed since the mutation batch began. Reopen it.");
        owner.CheckFresh();
    }

    private static async Task WriteZipAsync(
        ArchiveFilePanelSource owner, ArchiveMutationBatch batch,
        string temporary, CancellationToken cancellationToken)
    {
        using var sourceStream = File.OpenRead(owner.ArchiveFile);
        using var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        var originalZipEntries = sourceZip.Entries.ToDictionary(
            entry => ArchivePath.NormalizeEntry(entry.FullName), StringComparer.Ordinal);

        await using var output = new FileStream(temporary, FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
        using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in batch.Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory && !entry.Explicit)
                    continue;
                string name = entry.Path.TrimStart('/') + (entry.IsDirectory ? "/" : "");
                ZipArchiveEntry destination = writer.CreateEntry(name,
                    entry.IsDirectory ? CompressionLevel.NoCompression : CompressionLevel.Optimal);

                if (entry.OriginalPath is { } previous &&
                    originalZipEntries.TryGetValue(previous, out var originalEntry))
                {
                    destination.LastWriteTime = originalEntry.LastWriteTime;
                    destination.ExternalAttributes = originalEntry.ExternalAttributes;
                }
                else
                {
                    // DOS timestamps in ZIP have a supported range of 1980-2107.
                    DateTime time = entry.Modified;
                    if (time.Year < 1980 || time.Year > 2107)
                        time = new DateTime(1980, 1, 1);
                    destination.LastWriteTime = new DateTimeOffset(time);
                }

                if (entry.IsDirectory)
                    continue;

                await using Stream input = entry.StagingFile is { } staged
                    ? new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read,
                        BufferSize, FileOptions.Asynchronous)
                    : await batch.OpenReadAsync(entry.Path, cancellationToken).ConfigureAwait(false);
                using Stream target = destination.Open();
                long copied = await CopyExactAsync(input, target, cancellationToken).ConfigureAwait(false);
                if (copied != entry.Size)
                    throw new InvalidDataException("An archive entry changed size during rebuild: " + entry.Path);
            }
        }
        output.Flush(flushToDisk: true);
    }

    private static async Task<long> CopyExactAsync(
        Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        long copied = 0;
        while (true)
        {
            int n = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (n == 0) return copied;
            copied = checked(copied + n);
            await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateIndex(ArchiveMutationBatch batch, ArchiveEntryIndex index)
    {
        var expected = batch.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var actual = index.Nodes.Where(entry => entry.Path != "/").ToDictionary(
            entry => entry.Path, StringComparer.Ordinal);
        if (actual.Count != expected.Count)
            throw new InvalidDataException("Rebuilt archive entry count differs from staging.");
        foreach (var entry in expected.Values)
        {
            if (!actual.TryGetValue(entry.Path, out var actualEntry) ||
                actualEntry.IsDirectory != entry.IsDirectory ||
                (!entry.IsDirectory && actualEntry.Size != entry.Size))
                throw new InvalidDataException("Archive verification mismatch: " + entry.Path);
        }
    }

    private static async Task VerifyChangedFilesAsync(
        string archivePath, ArchiveMutationBatch batch, CancellationToken cancellationToken)
    {
        foreach (var entry in batch.Entries)
        {
            if (entry.StagingFile is null) continue;
            cancellationToken.ThrowIfCancellationRequested();
            await using var staged = new FileStream(entry.StagingFile, FileMode.Open,
                FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            byte[] expected = await SHA256.HashDataAsync(staged, cancellationToken).ConfigureAwait(false);
            var actualNode = new ArchiveNode(entry.Path, false, entry.Size, entry.Modified, -1, true);
            await using var reopened = SharpCompressArchiveReader.OpenEntry(
                archivePath, "ZIP", actualNode, cancellationToken);
            byte[] actual = await SHA256.HashDataAsync(reopened, cancellationToken).ConfigureAwait(false);
            if (!expected.AsSpan().SequenceEqual(actual))
                throw new InvalidDataException("Rebuilt archive entry content differs: " + entry.Path);
        }
    }
}
