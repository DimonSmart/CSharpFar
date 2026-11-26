using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>Shared publication boundary for complete file contents and provider batches.</summary>
public static class SafeFileWriteService
{
    public static async Task<string?> WriteLocalAsync(string path, byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var expected = SafeLocalFilePublisher.CaptureDestination(path);
        if (expected is { } existing && (existing.Attributes & FileAttributes.ReadOnly) != 0)
            throw new IOException("The file is read-only.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staging = SafeLocalFilePublisher.CreateStagingPath(path);
        bool owned = false;
        try
        {
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (new FileInfo(staging).Length != bytes.LongLength)
                throw new IOException($"Incomplete write to '{staging}'.");
            cancellationToken.ThrowIfCancellationRequested();
            string? backup = SafeLocalFilePublisher.PublishChecked(staging, path, expected);
            return backup is null ? null : $"File saved, but backup cleanup failed: {backup}";
        }
        catch (Exception error)
        {
            if (owned)
            {
                try { SafeLocalFilePublisher.DeleteStagingPath(staging); }
                catch (Exception cleanup)
                {
                    throw new IOException($"Write failed; staging cleanup failed: '{staging}'.",
                        new AggregateException(error, cleanup));
                }
            }
            throw;
        }
    }

    public static async Task<string?> WriteDocumentAsync(IFilePanelSource source, string path,
        byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (source.SourceId == PanelSourceId.Local)
            return await WriteLocalAsync(source.NormalizePath(path), bytes, cancellationToken).ConfigureAwait(false);

        Task<long> Write(Stream stream) => WriteBytesAsync(stream, bytes, cancellationToken);
        if (source is IFileOperationBatchProvider batching)
        {
            var location = new PanelLocation(source.SourceId, path);
            var plan = new FileOperationPlan([new FileOperationPlanItem(location, location, false)]);
            await ExecuteBatchAsync(batching, plan, async batch =>
            {
                var existing = batch.GetItem(path, cancellationToken);
                if (existing is not null && (existing.IsDirectory || existing.Attributes.HasFlag(FileAttributes.ReadOnly)))
                    throw new IOException("The destination is a directory or read-only.");
                await WriteProviderAsync(batch, path, existing, bytes.LongLength, Write,
                    keepPartial: false, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return await WriteProviderAsync(source, path, source.GetItem(path, cancellationToken),
            bytes.LongLength, Write, keepPartial: false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> WriteBytesAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes.LongLength;
    }

    internal static bool SameDestination(FilePanelItem? expected, FilePanelItem? actual) =>
        expected is null ? actual is null : actual is not null &&
        expected.Size == actual.Size && expected.LastWriteTime == actual.LastWriteTime &&
        expected.Attributes == actual.Attributes && expected.IsDirectory == actual.IsDirectory;

    internal static async Task<string?> WriteProviderAsync(IFilePanelSource source, string path,
        FilePanelItem? expected, long? expectedLength, Func<Stream, Task<long>> write,
        bool keepPartial, CancellationToken cancellationToken,
        Func<string, string, string?>? replaceLocal = null)
    {
        bool batch = source is IFileOperationBatch;
        bool local = !batch && source.SourceId == PanelSourceId.Local;
        var safe = source as ISafeNewFilePublicationProvider;
        if (!batch && !local && (safe is null || expected is not null))
            throw new NotSupportedException($"Provider '{source.DisplayName}' cannot safely publish {(expected is null ? "a new file" : "a replacement file")}.");
        if (!SameDestination(expected, source.GetItem(path, cancellationToken)))
            throw new IOException($"Destination changed before writing: '{path}'.");

        var localExpected = local ? SafeLocalFilePublisher.CaptureDestination(path) : null;
        string staging = batch ? path : local ? SafeLocalFilePublisher.CreateStagingPath(path) :
            source.CombinePath(source.GetParentPath(path) ?? throw new IOException("No destination directory."),
                $".{source.GetFileName(path)}.csharpfar-{Guid.NewGuid():N}.tmp");
        bool owned = false;
        bool publishing = false;
        try
        {
            long written;
            await using (var stream = batch || local
                ? await source.OpenWriteAsync(staging, batch && expected is not null, cancellationToken).ConfigureAwait(false)
                : await safe!.OpenStagingWriteAsync(staging, cancellationToken).ConfigureAwait(false))
            {
                owned = !batch;
                written = await write(stream).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (expectedLength is long length && length != written)
                throw new IOException($"Incomplete write to '{staging}' ({written}/{length} bytes).");
            var stagedItem = source.GetItem(staging, cancellationToken);
            if (stagedItem is null || stagedItem.IsDirectory ||
                (stagedItem.Size is long actual && actual != written))
                throw new IOException($"Staged file verification failed: '{staging}'.");
            cancellationToken.ThrowIfCancellationRequested();
            if (batch) return null;
            if (local)
            {
                string? backup = SafeLocalFilePublisher.PublishChecked(staging, path, localExpected, replaceLocal);
                return backup is null ? null : $"File published, but backup cleanup failed: {backup}";
            }

            publishing = true;
            await safe!.RenameWithoutOverwriteAsync(staging, path, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception error)
        {
            if (publishing)
            {
                // A lost reply can mean the rename already happened. Inspect but never
                // infer ownership of the final name from its size or existence alone.
                string observation;
                try
                {
                    bool temporaryExists = source.GetItem(staging, CancellationToken.None) is not null;
                    bool destinationExists = source.GetItem(path, CancellationToken.None) is not null;
                    observation = $"Staging exists: {temporaryExists}; destination exists: {destinationExists}.";
                    if (temporaryExists && !keepPartial)
                        await source.DeleteAsync(staging, recursive: false, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception inspection)
                {
                    throw new UnknownFilePublicationException(path, staging,
                        "State inspection or cleanup failed.", new AggregateException(error, inspection));
                }
                throw new UnknownFilePublicationException(path, staging, observation, error);
            }

            if (owned && !keepPartial)
            {
                try
                {
                    if (local) SafeLocalFilePublisher.DeleteStagingPath(staging);
                    else if (source.GetItem(staging, CancellationToken.None) is not null)
                        await source.DeleteAsync(staging, recursive: false, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    throw new IOException($"Write failed; staging cleanup failed: '{staging}'.",
                        new AggregateException(error, cleanup));
                }
            }
            if (owned && keepPartial && error is not OperationCanceledException)
                throw new IOException($"Write failed; partial data retained at '{staging}'.", error);
            throw;
        }
    }

    internal static async Task ExecuteBatchAsync(IFileOperationBatchProvider provider, FileOperationPlan plan,
        Func<IFileOperationBatch, Task> prepare, CancellationToken cancellationToken)
    {
        await using var batch = await provider.BeginBatchAsync(plan, cancellationToken).ConfigureAwait(false);
        try
        {
            await prepare(batch).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await batch.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception abort)
            {
                throw new IOException("File transaction failed and abort failed.", new AggregateException(error, abort));
            }
            throw;
        }
    }
}

public sealed class UnknownFilePublicationException(string destination, string staging, string observation, Exception cause)
    : IOException($"Publication outcome is unknown for '{destination}'. Staging: '{staging}'. {observation}", cause)
{
    public string DestinationPath { get; } = destination;
    public string StagingPath { get; } = staging;
}
