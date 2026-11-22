using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

public sealed partial class FileOperationService
{
    // Owns the delete preflight and execution for local and provider-backed paths.
    // FileOperationService retains only dispatch and the shared DeletePath entry point
    // used by move rollback/fallback paths.
    private sealed class DeleteExecutor(FileOperationService owner)
    {
        private IFilePanelSourceRegistry _sources => owner._sources;
        private FileOperationServiceDependencies _dependencies => owner._dependencies;
        private IFileSystemPlatformOperations _platformOperations => owner._platformOperations;

    public async Task ExecuteProviderAsync(
        FileOperationRequest request,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var sources = RequireSourceLocations(request);
        var plan = BuildProviderDeletePlan(sources, state, cancellationToken);
        state.SetTotals(plan.TotalBytes, plan.Items.Count);
        state.StartProgressTimer();

        foreach (var group in plan.Items.GroupBy(item => item.Location.SourceId))
        {
            var source = _sources.GetSource(group.Key);
            var items = group.Where(item => !group.Any(parent =>
                parent != item && ProviderPathRelations.IsDescendantOf(
                    source, item.Location.SourcePath, parent.Location.SourcePath))).ToArray();

            if (source is IFileOperationBatchProvider batching)
            {
                var publicPlan = new FileOperationPlan(items.Select(item =>
                    new FileOperationPlanItem(item.Location, item.Location, true)).ToArray());
                await using var batch = await batching.BeginBatchAsync(publicPlan, cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    await DeleteProviderItemsAsync(items, batch, state, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    state.ReportCommitting(items[0].Location.SourcePath);
                    await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { /* Preserve the primary error. */ }
                    throw;
                }
            }
            else
            {
                await DeleteProviderItemsAsync(items, source, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DeleteProviderItemsAsync(
        IReadOnlyList<ProviderDeletePlanItem> items,
        IFilePanelSource source,
        OperationState state,
        CancellationToken cancellationToken)
    {
        foreach (var plannedItem in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = plannedItem.Location;
            var item = source.GetItem(location.SourcePath, cancellationToken);
            if (item is null)
            {
                state.ReportDeleting(location.SourcePath, 0, 0);
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            long bytes = plannedItem.Size;
            state.ReportDeleting(location.SourcePath, 0, bytes);
            await source.DeleteAsync(location.SourcePath, item.IsDirectory, cancellationToken)
                .ConfigureAwait(false);
            state.DeletedCount++;
            state.AddBytes(bytes);
            state.CompleteItem();
            state.ReportDeleting(location.SourcePath, bytes, bytes);
        }
    }


    public void ExecuteLocal(FileOperationRequest request, OperationState state, CancellationToken cancellationToken)
    {
        var plan = BuildDeletePlan(request.Sources, state, cancellationToken);
        state.SetTotals(plan.TotalBytes, plan.Items.Count);
        state.StartProgressTimer();

        foreach (var plannedItem in plan.Items)
        {
            string path = plannedItem.Path;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    state.ReportDeleting(path, 0, 0);
                    state.SkippedCount++;
                    state.CompleteItem();
                    continue;
                }

                long bytes = plannedItem.Size;
                state.ReportDeleting(path, 0, bytes);
                DeletePath(path, request.Options.UseRecycleBinForDelete);
                state.DeletedCount++;
                state.AddBytes(bytes);
                state.CompleteItem();
                state.ReportDeleting(path, bytes, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                state.Errors.Add(new FileOperationItemError { Path = path, Message = ex.Message });
            }
        }
    }


    public void DeletePath(string path, bool useRecycleBin)
    {
        _dependencies.BeforeDeletePath(path);

        if (File.Exists(path))
            _platformOperations.DeleteFile(path, useRecycleBin);
        else if (Directory.Exists(path))
            _platformOperations.DeleteDirectory(path, recursive: true, useRecycleBin);
    }


    private static DeletePlan BuildDeletePlan(
        IReadOnlyList<string> sources,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var items = new List<DeletePlanItem>();
        int files = 0;
        int folders = 0;
        long bytes = 0;

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.ReportScanning(source, files, folders, bytes);
            long size = ScanLocalDeleteSource(source, state, ref files, ref folders, ref bytes, cancellationToken);
            items.Add(new DeletePlanItem(source, size));
        }

        return new DeletePlan(items, bytes);
    }

    private static long ScanLocalDeleteSource(
        string path,
        OperationState state,
        ref int files,
        ref int folders,
        ref long bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            long size = GetFileSize(path);
            files++;
            bytes += size;
            state.ReportScanning(path, files, folders, bytes);
            return size;
        }

        if (!Directory.Exists(path))
            return 0;

        folders++;
        state.ReportScanning(path, files, folders, bytes);
        long total = 0;
        foreach (string directory in Directory.EnumerateDirectories(path))
            total += ScanLocalDeleteSource(directory, state, ref files, ref folders, ref bytes, cancellationToken);
        foreach (string file in Directory.EnumerateFiles(path))
            total += ScanLocalDeleteSource(file, state, ref files, ref folders, ref bytes, cancellationToken);
        return total;
    }


    private DeleteProviderPlan BuildProviderDeletePlan(
        IReadOnlyList<PanelLocation> sources,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var items = new List<ProviderDeletePlanItem>();
        int files = 0;
        int folders = 0;
        long bytes = 0;

        foreach (PanelLocation source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.ReportScanning(source.SourcePath, files, folders, bytes);
            long size = ScanProviderDeleteSource(source, state, ref files, ref folders, ref bytes, cancellationToken);
            items.Add(new ProviderDeletePlanItem(source, size));
        }

        return new DeleteProviderPlan(items, bytes);
    }


    private long ScanProviderDeleteSource(
        PanelLocation location,
        OperationState state,
        ref int files,
        ref int folders,
        ref long bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _sources.GetSource(location.SourceId);
        var item = source.GetItem(location.SourcePath, cancellationToken);
        if (item is null)
            return 0;

        if (!item.IsDirectory)
        {
            long size = item.Size ?? 0;
            files++;
            bytes += size;
            state.ReportScanning(location.SourcePath, files, folders, bytes);
            return size;
        }

        folders++;
        state.ReportScanning(location.SourcePath, files, folders, bytes);
        long total = 0;
        foreach (var child in source.EnumerateDirectory(location.SourcePath, cancellationToken).Where(i => !i.IsParentDirectory))
            total += ScanProviderDeleteSource(child.Location, state, ref files, ref folders, ref bytes, cancellationToken);
        return total;
    }


    private sealed record DeletePlanItem(string Path, long Size);
    private sealed record DeletePlan(IReadOnlyList<DeletePlanItem> Items, long TotalBytes);
    private sealed record ProviderDeletePlanItem(PanelLocation Location, long Size);
    private sealed record DeleteProviderPlan(IReadOnlyList<ProviderDeletePlanItem> Items, long TotalBytes);

    }
}
