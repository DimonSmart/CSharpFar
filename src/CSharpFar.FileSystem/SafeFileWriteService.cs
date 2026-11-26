using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

/// <summary>Shared publication boundary for complete file contents and provider batches.</summary>
public static class SafeFileWriteService
{
    public static Task<string?> WriteLocalAsync(string path, byte[] bytes,
        CancellationToken cancellationToken = default) =>
        WriteLocalAsync(path, bytes, SafeLocalFilePublisher.CaptureDestination(Path.GetFullPath(path)), cancellationToken);

    public static Task<string?> WriteLocalAsync(string path, byte[] bytes,
        SafeLocalFilePublisher.DestinationSnapshot? expected, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (expected is { } existing && (existing.Attributes & FileAttributes.ReadOnly) != 0)
            throw new IOException("The file is read-only.");
        return WriteLocalContentsAsync(path, expected, bytes.LongLength,
            stream => WriteBytesAsync(stream, bytes, cancellationToken), false, cancellationToken);
    }

    private static async Task<string?> WriteLocalContentsAsync(string path,
        SafeLocalFilePublisher.DestinationSnapshot? expected, long? expectedLength,
        Func<Stream, Task<long>> write, bool keepPartial, CancellationToken cancellationToken,
        Func<string, string, string?>? replaceLocal = null, Action<string, string>? createLocal = null)
    {
        if (SafeLocalFilePublisher.CaptureDestination(path) != expected)
            throw new IOException($"Destination changed before writing: '{path}'.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staging = SafeLocalFilePublisher.CreateStagingPath(path);
        bool owned = false;
        try
        {
            long written;
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                written = await write(stream).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if ((expectedLength is long length && length != written) || new FileInfo(staging).Length != written)
                throw new IOException($"Incomplete write to '{staging}'.");
            cancellationToken.ThrowIfCancellationRequested();
            string? backup = SafeLocalFilePublisher.PublishChecked(staging, path, expected, replaceLocal, createLocal);
            return backup is null ? null : $"File published, but backup cleanup failed: {backup}";
        }
        catch (Exception error)
        {
            if (owned && !keepPartial)
            {
                try { SafeLocalFilePublisher.DeleteStagingPath(staging); }
                catch (Exception cleanup)
                {
                    throw new IOException($"Write failed; staging cleanup failed: '{staging}'.",
                        new AggregateException(error, cleanup));
                }
            }
            if (owned && keepPartial)
                throw PartialFailure(staging, error, cancellationToken);
            throw;
        }
    }

    private static Exception PartialFailure(string staging, Exception error, CancellationToken cancellationToken) =>
        error is OperationCanceledException
            ? new OperationCanceledException($"Partial data retained at '{staging}'.", error, cancellationToken)
            : new IOException($"Write failed; partial data retained at '{staging}'.", error);

    public static async Task<string?> WriteDocumentAsync(IFilePanelSource source, string path,
        byte[] bytes, CancellationToken cancellationToken = default) =>
        await WriteDocumentAsync(source, path, bytes, await source.AsAsync().GetItemAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public static async Task<string?> WriteDocumentAsync(IFilePanelSource source, string path,
        byte[] bytes, FilePanelItem? expected, CancellationToken cancellationToken = default)
    {
        if (expected is not null && (expected.IsDirectory || expected.Attributes.HasFlag(FileAttributes.ReadOnly)))
            throw new IOException("The destination is a directory or read-only.");

        Task<long> Write(Stream stream) => WriteBytesAsync(stream, bytes, cancellationToken);
        if (source is IFileOperationBatchProvider batching)
        {
            var location = new PanelLocation(source.SourceId, path);
            var plan = new FileOperationPlan([new FileOperationPlanItem(location, location, false)]);
            return await ExecuteBatchAsync(batching, plan, async batch =>
            {
                await WriteProviderAsync(batch, path, expected, bytes.LongLength, Write,
                    keepPartial: false, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        return await WriteProviderAsync(source, path, expected,
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
        Func<string, string, string?>? replaceLocal = null, Action<string, string>? createLocal = null)
    {
        bool batch = source is IFileOperationBatch;
        bool local = !batch && source.SourceId == PanelSourceId.Local;
        var safe = source as ISafeNewFilePublicationProvider;
        if (!batch && !local && (safe is null || expected is not null))
            throw new NotSupportedException($"Provider '{source.DisplayName}' cannot safely publish {(expected is null ? "a new file" : "a replacement file")}.");
        if (!SameDestination(expected, await source.AsAsync().GetItemAsync(path, cancellationToken).ConfigureAwait(false)))
            throw new IOException($"Destination changed before writing: '{path}'.");

        if (local)
        {
            path = source.NormalizePath(path);
            var localExpected = SafeLocalFilePublisher.CaptureDestination(path);
            if ((expected is null) != (localExpected is null) ||
                (expected is not null && localExpected is { } version &&
                    (expected.Size != version.Length || expected.LastWriteTime.ToUniversalTime() != version.LastWriteTimeUtc ||
                     expected.Attributes != version.Attributes)))
                throw new IOException($"Destination changed before writing: '{path}'.");
            return await WriteLocalContentsAsync(path, localExpected, expectedLength, write, keepPartial,
                cancellationToken, replaceLocal, createLocal).ConfigureAwait(false);
        }

        string staging = batch ? path :
            source.CombinePath(source.GetParentPath(path) ?? throw new IOException("No destination directory."),
                $".{source.GetFileName(path)}.csharpfar-{Guid.NewGuid():N}.tmp");
        bool owned = false;
        bool publishing = false;
        try
        {
            long written;
            await using (var stream = batch
                ? await source.AsAsync().OpenWriteAsync(staging, batch && expected is not null, cancellationToken).ConfigureAwait(false)
                : await safe!.OpenStagingWriteAsync(staging, cancellationToken).ConfigureAwait(false))
            {
                owned = !batch;
                written = await write(stream).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (expectedLength is long length && length != written)
                throw new IOException($"Incomplete write to '{staging}' ({written}/{length} bytes).");
            var stagedItem = await source.AsAsync().GetItemAsync(staging, cancellationToken).ConfigureAwait(false);
            if (stagedItem is null || stagedItem.IsDirectory ||
                (stagedItem.Size is long actual && actual != written))
                throw new IOException($"Staged file verification failed: '{staging}'.");
            cancellationToken.ThrowIfCancellationRequested();
            if (batch) return null;

            publishing = true;
            await safe!.RenameWithoutOverwriteAsync(staging, path, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception error)
        {
            if (publishing && source is not IRecoverableEntryRenameProvider)
            {
                // A lost reply can mean the rename already happened. Inspect but never
                // infer ownership of the final name from its size or existence alone.
                string observation;
                try
                {
                    bool temporaryExists = await source.AsAsync().GetItemAsync(staging, CancellationToken.None).ConfigureAwait(false) is not null;
                    bool destinationExists = await source.AsAsync().GetItemAsync(path, CancellationToken.None).ConfigureAwait(false) is not null;
                    observation = $"Staging exists: {temporaryExists}; destination exists: {destinationExists}.";
                    if (temporaryExists && !destinationExists)
                        publishing = false;
                }
                catch (Exception inspection)
                {
                    throw new UnknownFilePublicationException(path, staging,
                        "State inspection or cleanup failed.", new AggregateException(error, inspection));
                }
                if (publishing)
                    throw new UnknownFilePublicationException(path, staging, observation, error);
            }

            if (owned && !keepPartial)
            {
                try
                {
                    if (await source.AsAsync().GetItemAsync(staging, CancellationToken.None).ConfigureAwait(false) is not null)
                        await source.AsAsync().DeleteAsync(staging, recursive: false, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    throw new IOException($"Write failed; staging cleanup failed: '{staging}'.",
                        new AggregateException(error, cleanup));
                }
            }
            if (owned && keepPartial)
                throw PartialFailure(staging, error, cancellationToken);
            throw;
        }
    }

    internal static async Task<string?> ExecuteBatchAsync(IFileOperationBatchProvider provider, FileOperationPlan plan,
        Func<IFileOperationBatch, Task> prepare, CancellationToken cancellationToken)
    {
        var batch = await provider.BeginBatchAsync(plan, cancellationToken).ConfigureAwait(false);
        try
        {
            await prepare(batch).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception abort) { errors.Add(abort); }
            try { await batch.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { errors.Add(cleanup); }
            if (errors.Count > 1)
                throw new IOException("File transaction failed and recovery or cleanup failed.",
                    new AggregateException(errors));
            throw;
        }

        var warnings = new List<string>();
        try { await batch.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup)
        {
            warnings.Add("Transaction committed, but disposal failed: " + cleanup.Message);
        }
        if (batch is IFileOperationBatchDiagnostics diagnostics)
            warnings.AddRange(diagnostics.Warnings);
        return warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
    }
}

public sealed class UnknownFilePublicationException(string destination, string staging, string observation, Exception cause)
    : IOException($"Publication outcome is unknown for '{destination}'. Staging: '{staging}'. {observation}", cause)
{
    public string DestinationPath { get; } = destination;
    public string StagingPath { get; } = staging;
}
