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

        if (source is not IFileOperationBatchProvider batching)
            throw new NotSupportedException($"Provider '{source.DisplayName}' cannot safely save this file.");
        var location = new PanelLocation(source.SourceId, path);
        var plan = new FileOperationPlan([new FileOperationPlanItem(location, location, false)]);
        await ExecuteBatchAsync(batching, plan, async batch =>
        {
            var existing = batch.GetItem(path, cancellationToken);
            if (existing is not null && (existing.IsDirectory || existing.Attributes.HasFlag(FileAttributes.ReadOnly)))
                throw new IOException("The destination is a directory or read-only.");
            await using (var stream = await batch.OpenWriteAsync(path, existing is not null, cancellationToken)
                .ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (batch.GetItem(path, cancellationToken)?.Size is long size && size != bytes.LongLength)
                throw new IOException($"Incomplete write to '{path}'.");
        }, cancellationToken).ConfigureAwait(false);
        return null;
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
