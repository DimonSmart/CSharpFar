using System.Diagnostics;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

public sealed partial class FileOperationService
{
    // Local and provider move share one owner and reuse the existing copy pipeline.
    private sealed class MoveExecutor(FileOperationService owner)
    {
        private IFilePanelSourceRegistry _sources => owner._sources;
        private FileOperationServiceDependencies _dependencies => owner._dependencies;

        public FileOperationPlan BuildLocalMovePreviewPlan(FileOperationRequest request, CancellationToken cancellationToken)
        {
            string destination = RequireDestination(request);
            bool singleRename = request.Sources.Count == 1 && !IsPath(destination);
            string effectiveDestination = singleRename ? Path.Combine(Path.GetDirectoryName(request.Sources[0])!, destination) : destination;
            var plan = BuildLocalMovePlan(request.Sources, effectiveDestination, singleRename, request.Options, request.UseDestinationTemplate, cancellationToken);
            return new FileOperationPlan(plan.Select(item => item.Mapping).ToArray());
        }

        public async Task MoveProviderAsync(
            FileOperationRequest request,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            ValidateMoveConflictDecision(request.Options.DefaultConflictDecision);
            var sources = RequireSourceLocations(request);
            var destination = RequireDestinationLocation(request);
            if (sources.Any(location =>
                location.SourceId != destination.SourceId &&
                !SamePhysicalContainer(_sources.GetSource(location.SourceId),
                    _sources.GetSource(destination.SourceId))))
            {
                await MoveAcrossProvidersAsync(request, conflictResolver, state, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var source = _sources.GetSource(sources[0].SourceId);
            var plan = BuildProviderMovePlan(source, sources, destination.SourcePath,
                request.Options, request.UseDestinationTemplate, cancellationToken);
            state.SetTotals(CalculateProviderPlanSize(plan), plan.Count);
            state.StartProgressTimer();

            if (source is IFileOperationBatchProvider batching)
            {
                await using var batch = await batching.BeginBatchAsync(
                    ToPublicPlan(plan, destination.SourceId), cancellationToken).ConfigureAwait(false);
                try
                {
                    await MoveProviderPlanItemsAsync(plan, batch, request.Options,
                        conflictResolver, state, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    state.ReportCommitting(destination.SourcePath);
                    await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { /* Preserve the original error. */ }
                    throw;
                }
            }
            else
            {
                await MoveProviderPlanItemsAsync(plan, source, request.Options,
                    conflictResolver, state, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task MoveProviderPlanItemsAsync(
            IReadOnlyList<ProviderOperationPlanItem> plan,
            IFilePanelSource source,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Item is null)
                {
                    state.SkippedCount++;
                    state.CompleteItem();
                    continue;
                }
                await MoveProviderItemAsync(source, item.SourcePath, item.TargetPath,
                    item.Item, options, conflictResolver, state, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task MoveAcrossProvidersAsync(
            FileOperationRequest request,
            IFileOperationConflictResolver resolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            ValidateProviderCopyOptions(request.Options);
            var sources = RequireSourceLocations(request);
            var destination = RequireDestinationLocation(request);
            var targetSource = _sources.GetSource(destination.SourceId);
            var sourceSource = _sources.GetSource(sources[0].SourceId);

            if (sources.Any(item => item.SourceId != sources[0].SourceId))
                throw new NotSupportedException("A move cannot span multiple source providers.");
            var fromRequired = PanelProviderCapabilities.CopyFrom |
                PanelProviderCapabilities.OpenRead | PanelProviderCapabilities.Delete;
            var toRequired = PanelProviderCapabilities.CopyTo | PanelProviderCapabilities.OpenWrite;
            if ((sourceSource.Capabilities & fromRequired) != fromRequired ||
                (targetSource.Capabilities & toRequired) != toRequired ||
                (sourceSource.SourceId != PanelSourceId.Local && sourceSource is not IFileOperationBatchProvider) ||
                (targetSource.SourceId != PanelSourceId.Local && targetSource is not IFileOperationBatchProvider))
                throw new NotSupportedException("Cross-provider move requires verified copy and delete capabilities.");

            var plan = owner.BuildProviderCopyPlan(sources, destination, request.Options,
                request.UseDestinationTemplate, cancellationToken, state);
            state.SetTotals(CalculateProviderPlanSize(plan), CountProviderPlanItems(plan));
            state.StartProgressTimer();
            var copiedFiles = new List<string>();
            var copiedFolders = new List<string>();

            if (targetSource is IFileOperationBatchProvider batching)
            {
                await using var batch = await batching.BeginBatchAsync(
                    ToPublicPlan(plan, destination.SourceId), cancellationToken).ConfigureAwait(false);
                try
                {
                    await owner.CopyProviderPlanItemsAsync(plan, batch, request.Options, resolver,
                        state, cancellationToken, copiedFiles, copiedFolders).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    state.ReportCommitting(destination.SourcePath);
                    await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { /* Keep the primary error. */ }
                    throw;
                }
            }
            else
            {
                // Ordinary local destination has no distributed rollback; do not
                // delete source until every requested copy attempt completes.
                await owner.CopyProviderPlanItemsAsync(plan, targetSource, request.Options, resolver,
                    state, cancellationToken, copiedFiles, copiedFolders).ConfigureAwait(false);
            }

            state.CopiedCount = 0; // These were copies made as part of a move.
            if (copiedFiles.Count == 0 && copiedFolders.Count == 0)
                return;
            if (state.SkippedCount != 0)
                state.PartiallyMoved = true;

            try
            {
                if (sourceSource is IFileOperationBatchProvider sourceBatching)
                {
                    int movedBeforeBatch = state.MovedCount;
                    var deletePlan = new FileOperationPlan(copiedFiles.Select(path =>
                    {
                        var location = new PanelLocation(sourceSource.SourceId, path);
                        return new FileOperationPlanItem(location, location, false);
                    }).ToArray());
                    await using var batch = await sourceBatching.BeginBatchAsync(deletePlan,
                        cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await RemoveCopiedSourcesAsync(batch, copiedFiles, copiedFolders,
                            state, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        state.ReportCommitting(sources[0].SourcePath);
                        await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        state.MovedCount = movedBeforeBatch;
                        try { await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false); }
                        catch { }
                        throw;
                    }
                }
                else
                {
                    await RemoveCopiedSourcesAsync(sourceSource, copiedFiles, copiedFolders,
                        state, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                state.PartiallyMoved = true;
                state.Cancelled = true;
                state.AddError(sources[0].SourcePath,
                    "Move was cancelled after destination publication; some source entries may remain.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The destination was already committed. Never attempt to undo it;
                // both copies may remain until the user explicitly resolves the issue.
                state.PartiallyMoved = true;
                state.AddError(sources[0].SourcePath,
                    "Destination was committed, but source removal failed: " + ex.Message);
            }
        }

        private static async Task RemoveCopiedSourcesAsync(
            IFilePanelSource source, IEnumerable<string> copiedFiles,
            IEnumerable<string> copiedFolders, OperationState state, CancellationToken cancellationToken)
        {
            foreach (string path in copiedFiles.Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.GetItem(path, cancellationToken) is null)
                    continue;
                await source.DeleteAsync(path, recursive: false, cancellationToken).ConfigureAwait(false);
                state.MovedCount++;
            }

            // Remove directories only when all remaining children were copied.
            // Skipped files are left in place along with their ancestors.
            foreach (string path in copiedFolders.Distinct(StringComparer.Ordinal)
                         .OrderByDescending(path => path.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.GetItem(path, cancellationToken) is not { IsDirectory: true })
                    continue;
                if (!source.EnumerateDirectory(path, cancellationToken)
                        .Any(item => !item.IsParentDirectory))
                    await source.DeleteAsync(path, recursive: false, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task MoveProviderItemAsync(
            IFilePanelSource source,
            string sourcePath,
            string destinationPath,
            FilePanelItem sourceItem,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            string targetPath = destinationPath;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ProviderPathRelations.PathsEqual(source, sourcePath, targetPath))
                {
                    state.CompleteItem();
                    return;
                }

                FilePanelItem? destinationItem = source.GetItem(targetPath, cancellationToken);
                if (destinationItem is not null)
                {
                    EnsureMoveTypeCompatibility(sourceItem, destinationItem);
                    FileOperationConflictDecision decision = ResolveProviderMoveConflict(
                        sourceItem,
                        destinationItem,
                        targetPath,
                        options,
                        conflictResolver,
                        state);

                    switch (decision.Mode)
                    {
                        case ConflictDecisionMode.Skip:
                        case ConflictDecisionMode.SkipAll:
                            state.SkippedCount++;
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.Rename:
                        case ConflictDecisionMode.RenameAll:
                            targetPath = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                                ? GenerateProviderName(source, targetPath, cancellationToken)
                                : decision.NewDestinationPath;
                            ValidateProviderMoveTarget(source, sourcePath, targetPath, sourceItem.IsDirectory);
                            continue;
                        case ConflictDecisionMode.Cancel:
                            throw new OperationCanceledException("File operation cancelled by user.");
                        case ConflictDecisionMode.Merge:
                            if (!sourceItem.IsDirectory || !destinationItem.IsDirectory)
                                throw new InvalidOperationException("Merge is only supported for directory conflicts.");
                            await MergeProviderDirectoriesAsync(
                                    source,
                                    sourcePath,
                                    targetPath,
                                    options,
                                    conflictResolver,
                                    state,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.Replace:
                            await ReplaceProviderItemAsync(
                                    source,
                                    sourcePath,
                                    targetPath,
                                    destinationItem,
                                    state,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            state.MovedCount++;
                            state.AddBytes(sourceItem.Size ?? 0);
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.Overwrite:
                        case ConflictDecisionMode.OverwriteAll:
                            if (sourceItem.IsDirectory)
                                throw new InvalidOperationException("Directory conflicts require an explicit Merge or Replace decision.");
                            await ReplaceProviderItemAsync(
                                    source,
                                    sourcePath,
                                    targetPath,
                                    destinationItem,
                                    state,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            state.MovedCount++;
                            state.AddBytes(sourceItem.Size ?? 0);
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.OnlyNewer:
                            throw new InvalidOperationException("Only newer is only supported for copy operations.");
                        default:
                            throw new InvalidOperationException($"Unsupported move conflict decision: {decision.Mode}.");
                    }
                }

                await EnsureProviderDestinationDirectoryAsync(source, targetPath, cancellationToken).ConfigureAwait(false);
                await source.RenameAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                state.MovedCount++;
                state.AddBytes(sourceItem.Size ?? 0);
                state.CompleteItem();
                return;
            }
        }

        private static FileOperationConflictDecision ResolveProviderMoveConflict(
            FilePanelItem sourceItem,
            FilePanelItem destinationItem,
            string destinationPath,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state) =>
            state.Conflicts.Resolve(
                options,
                conflictResolver,
                () => BuildProviderConflict(sourceItem, destinationItem, destinationPath),
                requiresExplicitDirectoryDecision: sourceItem.IsDirectory && destinationItem.IsDirectory);

        private async Task MergeProviderDirectoriesAsync(
            IFilePanelSource source,
            string sourcePath,
            string destinationPath,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            PanelProviderCapabilities required =
                PanelProviderCapabilities.Enumerate |
                PanelProviderCapabilities.Rename |
                PanelProviderCapabilities.Delete;
            if ((source.Capabilities & required) != required)
                throw new NotSupportedException("This provider cannot safely merge directories.");

            FilePanelItem[] children = source
                .EnumerateDirectory(sourcePath, cancellationToken)
                .Where(item => !item.IsParentDirectory)
                .ToArray();

            foreach (FilePanelItem child in children)
            {
                string childTarget = CombineProviderPath(source.SourceId, destinationPath, child.Name);
                await MoveProviderItemAsync(
                        source,
                        child.SourcePath,
                        childTarget,
                        child,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!source.EnumerateDirectory(sourcePath, cancellationToken).Any(item => !item.IsParentDirectory))
                await source.DeleteAsync(sourcePath, recursive: false, cancellationToken).ConfigureAwait(false);
        }

        private async Task ReplaceProviderItemAsync(
            IFilePanelSource source,
            string sourcePath,
            string destinationPath,
            FilePanelItem destinationItem,
            OperationState state,
            CancellationToken cancellationToken)
        {
            PanelProviderCapabilities required = PanelProviderCapabilities.Rename | PanelProviderCapabilities.Delete;
            if ((source.Capabilities & required) != required)
                throw new NotSupportedException("This provider cannot safely replace an existing destination.");

            string backupPath = GenerateProviderTemporaryPath(source, destinationPath, "backup", cancellationToken);
            await source.RenameAsync(destinationPath, backupPath, cancellationToken).ConfigureAwait(false);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await source.RenameAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception moveError)
            {
                try
                {
                    if (source.GetItem(destinationPath, CancellationToken.None) is null)
                    {
                        await source.RenameAsync(backupPath, destinationPath, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        throw new IOException(
                            $"Replacement failed and destination is occupied. Original destination was preserved as: {backupPath}");
                    }
                }
                catch (Exception rollbackError)
                {
                    throw new IOException(
                        $"Replace failed and rollback failed. Original destination was preserved as: {backupPath}",
                        new AggregateException(moveError, rollbackError));
                }

                throw;
            }

            try
            {
                await source.DeleteAsync(backupPath, destinationItem.IsDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                state.AddError(backupPath, $"Replacement succeeded, but backup cleanup failed: {cleanupError.Message}");
            }
        }

        private static string GenerateProviderTemporaryPath(
            IFilePanelSource source,
            string destinationPath,
            string kind,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                string candidate = destinationPath + $".csharpfar-replace-{kind}-{suffix}";
                if (source.GetItem(candidate, cancellationToken) is null)
                    return candidate;
            }
        }

        private Task<CopyOutcome> CopyLocalAsync(
            FileOperationRequest request,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken,
            string? rootTarget = null) =>
            owner.CopyProviderAsync(
                request, conflictResolver, state, cancellationToken,
                useLocalStrategy: true, rootTarget: rootTarget);

        public async Task MoveAsync(
            FileOperationRequest request,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            ValidateMoveConflictDecision(request.Options.DefaultConflictDecision);

            string destination = RequireDestination(request);
            bool singleRename = request.Sources.Count == 1 && !IsPath(destination);
            string effectiveDestination = singleRename
                ? Path.Combine(Path.GetDirectoryName(request.Sources[0])!, destination)
                : destination;
            var plan = BuildLocalMovePlan(
                request.Sources,
                effectiveDestination,
                singleRename,
                request.Options,
                request.UseDestinationTemplate,
                cancellationToken);
            bool wholeItemPureRename = IsWholeItemPureRename(plan, request);
            state.SetTotals(CalculateSourcesSize(request.Sources), request.Sources.Count);
            state.StartProgressTimer();

            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (HasFileMask(request.Options))
                {
                    CopyOutcome outcome = await CopyLocalAsync(
                        request with
                        {
                            Kind = FileOperationKind.Copy,
                            Sources = [item.SourcePath],
                            Destination = Path.GetDirectoryName(item.TargetPath)!,
                            UseDestinationTemplate = false,
                        },
                        conflictResolver,
                        state,
                        cancellationToken,
                        item.TargetPath).ConfigureAwait(false);

                    foreach (string copiedSource in outcome.SuccessfullyCopiedSources)
                    {
                        File.Delete(copiedSource);
                        state.MovedCount++;
                    }

                    continue;
                }

                await MoveLocalItemAsync(
                        item.SourcePath,
                        item.TargetPath,
                        request.Options,
                        conflictResolver,
                        state,
                        cancellationToken,
                        allowFallback: !wholeItemPureRename)
                    .ConfigureAwait(false);
            }
        }

        private static bool IsWholeItemPureRename(
            IReadOnlyList<LocalMovePlanItem> plan,
            FileOperationRequest request)
        {
            if (request.Sources.Count != 1 || plan.Count != 1 || HasFileMask(request.Options))
                return false;

            string? sourceParent = Path.GetDirectoryName(Path.GetFullPath(plan[0].SourcePath));
            string? targetParent = Path.GetDirectoryName(Path.GetFullPath(plan[0].TargetPath));
            return sourceParent is not null &&
                   targetParent is not null &&
                   PathsEqual(sourceParent, targetParent);
        }

        private async Task MoveLocalItemAsync(
            string source,
            string destination,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken,
            bool allowFallback)
        {
            string target = destination;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (PathsEqual(source, target))
                {
                    state.SkippedCount++;
                    state.CompleteItem();
                    return;
                }

                bool sourceIsFile = File.Exists(source);
                bool sourceIsDirectory = Directory.Exists(source);
                if (!sourceIsFile && !sourceIsDirectory)
                {
                    state.SkippedCount++;
                    state.CompleteItem();
                    return;
                }

                if (sourceIsDirectory && IsPathInside(target, source))
                    throw new IOException("Cannot move a directory into itself.");

                bool destinationIsFile = File.Exists(target);
                bool destinationIsDirectory = Directory.Exists(target);
                if (destinationIsFile || destinationIsDirectory)
                {
                    if ((sourceIsFile && destinationIsDirectory) || (sourceIsDirectory && destinationIsFile))
                        throw new IOException("Cannot overwrite a file with a directory or a directory with a file.");

                    FileOperationConflictDecision decision = ResolveLocalMoveConflict(
                        source,
                        target,
                        sourceIsDirectory,
                        destinationIsDirectory,
                        options,
                        conflictResolver,
                        state);

                    switch (decision.Mode)
                    {
                        case ConflictDecisionMode.Skip:
                        case ConflictDecisionMode.SkipAll:
                            state.SkippedCount++;
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.Cancel:
                            throw new OperationCanceledException("Move cancelled by user.");
                        case ConflictDecisionMode.Rename:
                        case ConflictDecisionMode.RenameAll:
                            target = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                                ? GenerateName(target)
                                : decision.NewDestinationPath;
                            continue;
                        case ConflictDecisionMode.Merge:
                            if (!sourceIsDirectory || !destinationIsDirectory)
                                throw new InvalidOperationException("Merge is only supported for directory conflicts.");
                            await MergeLocalDirectoriesAsync(
                                    source,
                                    target,
                                    options,
                                    conflictResolver,
                                    state,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            state.CompleteItem();
                            return;
                        case ConflictDecisionMode.Replace:
                            await ReplaceLocalAsync(
                                    source,
                                    target,
                                    sourceIsFile,
                                    options,
                                    conflictResolver,
                                    state,
                                    cancellationToken,
                                    allowFallback)
                                .ConfigureAwait(false);
                            CompleteLocalMove(source, target, sourceIsFile, state);
                            return;
                        case ConflictDecisionMode.Overwrite:
                        case ConflictDecisionMode.OverwriteAll:
                            if (sourceIsDirectory)
                                throw new InvalidOperationException("Directory conflicts require an explicit Merge or Replace decision.");
                            await ReplaceLocalAsync(
                                    source,
                                    target,
                                    sourceIsFile,
                                    options,
                                    conflictResolver,
                                    state,
                                    cancellationToken,
                                    allowFallback)
                                .ConfigureAwait(false);
                            CompleteLocalMove(source, target, sourceIsFile, state);
                            return;
                        case ConflictDecisionMode.OnlyNewer:
                            throw new InvalidOperationException("Only newer is only supported for copy operations.");
                        default:
                            throw new InvalidOperationException($"Unsupported move conflict decision: {decision.Mode}.");
                    }
                }

                DirectMoveResult directResult = RequiresCopyDeleteFallback(source, target)
                    ? DirectMoveResult.NotSupported
                    : TryMoveDirect(source, target, sourceIsFile);
                if (directResult == DirectMoveResult.Moved)
                {
                    CompleteLocalMove(source, target, sourceIsFile, state);
                    return;
                }

                if (!allowFallback)
                    throw new IOException("Direct rename is not supported for this source and destination.");

                CopyOutcome outcome = await CopyLocalAsync(
                    new FileOperationRequest
                    {
                        Kind = FileOperationKind.Copy,
                        Sources = [source],
                        Destination = Path.GetDirectoryName(target)!,
                        Options = options,
                        UseDestinationTemplate = false,
                    },
                    conflictResolver,
                    state,
                    cancellationToken,
                    target).ConfigureAwait(false);

                if (!outcome.CopiedSomething || !outcome.Completed)
                    return;

                owner.DeletePath(source, useRecycleBin: false);
                state.MovedCount++;
                return;
            }
        }

        private static FileOperationConflictDecision ResolveLocalMoveConflict(
            string source,
            string destination,
            bool sourceIsDirectory,
            bool destinationIsDirectory,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state) =>
            state.Conflicts.Resolve(
                options,
                conflictResolver,
                () => BuildConflict(source, destination),
                requiresExplicitDirectoryDecision: sourceIsDirectory && destinationIsDirectory);

        private async Task MergeLocalDirectoriesAsync(
            string source,
            string destination,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            string[] children = Directory.EnumerateFileSystemEntries(source).ToArray();
            foreach (string child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string childTarget = Path.Combine(destination, Path.GetFileName(child));
                await MoveLocalItemAsync(
                        child,
                        childTarget,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken,
                        allowFallback: true)
                    .ConfigureAwait(false);
            }

            if (!Directory.EnumerateFileSystemEntries(source).Any())
                Directory.Delete(source, recursive: false);
        }

        private async Task ReplaceLocalAsync(
            string source,
            string destination,
            bool sourceIsFile,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken,
            bool allowFallback)
        {
            if (RequiresCopyDeleteFallback(source, destination))
            {
                if (!allowFallback)
                    throw new IOException("Direct rename is not supported; copy/delete fallback is disabled for a pure rename.");

                await ReplaceLocalViaStagingAsync(
                        source,
                        destination,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            string backup = GenerateReplaceTemporaryPath(destination, "backup");
            MoveOwnedPath(destination, backup);

            DirectMoveResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = TryMoveDirect(source, destination, sourceIsFile);
            }
            catch (Exception moveError)
            {
                RestoreLocalBackupOrThrow(destination, backup, moveError);
                throw;
            }

            if (result == DirectMoveResult.NotSupported)
            {
                RestoreLocalBackupOrThrow(destination, backup, null);
                if (!allowFallback)
                    throw new IOException("Direct rename is not supported; copy/delete fallback is disabled for a pure rename.");

                await ReplaceLocalViaStagingAsync(
                        source,
                        destination,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            CleanupOwnedLocalPath(backup, state, "Replacement succeeded, but backup cleanup failed");
        }

        private async Task ReplaceLocalViaStagingAsync(
            string source,
            string destination,
            FileOperationOptions options,
            IFileOperationConflictResolver conflictResolver,
            OperationState state,
            CancellationToken cancellationToken)
        {
            string staging = GenerateReplaceTemporaryPath(destination, "new");
            string backup = GenerateReplaceTemporaryPath(destination, "backup");

            CopyOutcome copyOutcome = await CopyLocalAsync(
                new FileOperationRequest
                {
                    Kind = FileOperationKind.Copy,
                    Sources = [source],
                    Destination = Path.GetDirectoryName(staging)!,
                    Options = options,
                    UseDestinationTemplate = false,
                },
                conflictResolver,
                state,
                cancellationToken,
                staging).ConfigureAwait(false);

            if (!copyOutcome.CopiedSomething || !copyOutcome.Completed)
            {
                CleanupOwnedLocalPath(staging, state, "Staging copy failed and staging cleanup also failed");
                throw new IOException("Replacement staging copy failed; the original destination was not changed.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            MoveOwnedPath(destination, backup);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                MoveOwnedPath(staging, destination);
            }
            catch (Exception commitError)
            {
                RestoreLocalBackupOrThrow(destination, backup, commitError);
                CleanupOwnedLocalPath(staging, state, "Rollback succeeded, but staging cleanup failed");
                throw;
            }

            try
            {
                owner.DeletePath(source, useRecycleBin: false);
            }
            catch (Exception deleteError)
            {
                throw new IOException(
                    $"Replacement was committed, but the source could not be removed. New destination: {destination}. Original destination backup: {backup}",
                    deleteError);
            }

            CleanupOwnedLocalPath(backup, state, "Replacement succeeded, but backup cleanup failed");
        }

        private bool RequiresCopyDeleteFallback(string source, string destination) =>
            _dependencies.ForceMoveFallback(source, destination) ||
            IsKnownCrossFileSystemMove(source, destination);

        internal static bool IsKnownCrossFileSystemMove(string source, string destination)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(source));
            string? destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination));

            return !string.IsNullOrEmpty(sourceRoot) &&
                   !string.IsNullOrEmpty(destinationRoot) &&
                   !string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);
        }

        private DirectMoveResult TryMoveDirect(string source, string destination, bool sourceIsFile)
        {
            if (_dependencies.ForceMoveFallback(source, destination))
                return DirectMoveResult.NotSupported;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                if (sourceIsFile)
                    _dependencies.MoveFile(source, destination);
                else
                    _dependencies.MoveDirectory(source, destination);
                return DirectMoveResult.Moved;
            }
            catch (IOException ex) when (IsCrossDeviceMoveException(ex))
            {
                return DirectMoveResult.NotSupported;
            }
        }

        private static bool IsCrossDeviceMoveException(IOException exception)
        {
            int nativeCode = exception.HResult & 0xFFFF;
            return OperatingSystem.IsWindows()
                ? nativeCode == 17
                : nativeCode == 18 || exception.HResult == 18;
        }

        private void MoveOwnedPath(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(source))
                _dependencies.MoveFile(source, destination);
            else if (Directory.Exists(source))
                _dependencies.MoveDirectory(source, destination);
            else
                throw new IOException($"Path does not exist: {source}");
        }

        private void RestoreLocalBackupOrThrow(string destination, string backup, Exception? originalError)
        {
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new IOException(
                    $"Replace failed. Original destination was preserved as: {backup}. The destination path is occupied and was not deleted.",
                    originalError);
            }

            try
            {
                MoveOwnedPath(backup, destination);
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    $"Replace failed and rollback failed. Original destination was preserved as: {backup}",
                    originalError is null ? rollbackError : new AggregateException(originalError, rollbackError));
            }
        }

        private void CleanupOwnedLocalPath(string path, OperationState state, string message)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;

            try
            {
                owner.DeletePath(path, useRecycleBin: false);
            }
            catch (Exception cleanupError)
            {
                state.AddError(path, $"{message}: {cleanupError.Message}. Preserved at: {path}");
            }
        }

        private static string GenerateReplaceTemporaryPath(string destination, string kind)
        {
            while (true)
            {
                string suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                string candidate = destination + $".csharpfar-replace-{kind}-{suffix}";
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
            }
        }

        private static void CompleteLocalMove(
            string source,
            string destination,
            bool sourceWasFile,
            OperationState state)
        {
            long bytes = sourceWasFile && File.Exists(destination) ? GetFileSize(destination) : 0;
            state.MovedCount++;
            state.AddBytes(bytes);
            state.CompleteItem();
            state.Report(source, destination, bytes, bytes);
        }

        private static bool HasFileMask(FileOperationOptions options) => !string.IsNullOrWhiteSpace(options.FileMask);

        private static DestinationTemplateContext CreateLocalTemplateContext(string sourcePath)
        {
            bool isDirectory = Directory.Exists(sourcePath);
            DateTime lastWriteTime = isDirectory
                ? Directory.GetLastWriteTime(sourcePath)
                : File.GetLastWriteTime(sourcePath);
            return new DestinationTemplateContext(Path.GetFileName(sourcePath), isDirectory, lastWriteTime, LocalPathSeparators);
        }

        private static List<LocalMovePlanItem> BuildLocalMovePlan(
            IReadOnlyList<string> sources,
            string destination,
            bool singleRename,
            FileOperationOptions options,
            bool useDestinationTemplate,
            CancellationToken cancellationToken)
        {
            DestinationPattern pattern = useDestinationTemplate
                ? default
                : DestinationPattern.Parse(destination, PanelSourceId.Local);
            DestinationTemplate? template = useDestinationTemplate ? DestinationTemplate.Parse(destination) : null;
            var plan = new List<LocalMovePlanItem>(sources.Count);
            bool hasMask = HasFileMask(options);
            var matcher = new FarMaskMatcher();
            var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (hasMask && !IsSelectedMoveSource(source, options.FileMask!, matcher, groups, cancellationToken))
                    continue;

                string target = template is not null
                    ? template.Evaluate(CreateLocalTemplateContext(source))
                    : pattern.HasWildcards
                    ? Path.Combine(string.IsNullOrEmpty(pattern.ParentPath) ? Path.GetDirectoryName(source)! : pattern.ParentPath, pattern.TransformName(Path.GetFileName(source)))
                    : singleRename
                        ? destination
                        : Path.Combine(destination, Path.GetFileName(source));
                plan.Add(new LocalMovePlanItem(source, target));
            }

            ValidateLocalMovePlan(plan, useDestinationTemplate || pattern.HasWildcards);
            return plan;
        }

        private static bool IsSelectedMoveSource(
            string source,
            string fileMask,
            FarMaskMatcher matcher,
            IReadOnlyDictionary<string, MaskGroup> groups,
            CancellationToken cancellationToken)
        {
            if (File.Exists(source))
                return matcher.IsMatch(fileMask, Path.GetFileName(source), groups);

            return Directory.Exists(source) && DirectoryContainsMatchingFile(source, fileMask, matcher, groups, cancellationToken);
        }

        private static bool DirectoryContainsMatchingFile(
            string directory,
            string fileMask,
            FarMaskMatcher matcher,
            IReadOnlyDictionary<string, MaskGroup> groups,
            CancellationToken cancellationToken)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matcher.IsMatch(fileMask, Path.GetFileName(file), groups))
                    return true;
            }

            return false;
        }

        private static long CalculateSourcesSize(IReadOnlyList<string> sources) =>
            sources.Sum(CalculateSourceSize);

        private static long CalculateSourceSize(string source) =>
            File.Exists(source)
                ? GetFileSize(source)
                : Directory.Exists(source)
                    ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(GetFileSize)
                    : 0;

        private static void ValidateMoveConflictDecision(ConflictDecisionMode mode)
        {
            if (mode == ConflictDecisionMode.OnlyNewer)
                throw new InvalidOperationException("Only newer is only supported for copy operations.");
        }

        public List<ProviderOperationPlanItem> BuildProviderMovePlan(
            IFilePanelSource source,
            IReadOnlyList<PanelLocation> sources,
            string destinationPath,
            FileOperationOptions options,
            bool useDestinationTemplate,
            CancellationToken cancellationToken)
        {
            DestinationPattern destinationPattern = useDestinationTemplate
                ? default
                : DestinationPattern.Parse(destinationPath, source.SourceId);
            DestinationTemplate? template = useDestinationTemplate ? DestinationTemplate.Parse(destinationPath) : null;
            IReadOnlyCollection<char>? destinationPathSeparators = template is null ? null : source.PathSeparators;
            var plan = new List<ProviderOperationPlanItem>(sources.Count);
            bool hasMask = HasFileMask(options);
            var matcher = new FarMaskMatcher();
            IReadOnlyDictionary<string, MaskGroup> groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (PanelLocation location in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FilePanelItem? item = source.GetItem(location.SourcePath, cancellationToken);
                string normalizedSourcePath = source.NormalizePath(location.SourcePath);
                if (item is null)
                {
                    plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, null, string.Empty, source.SourceId));
                    continue;
                }

                string targetPath = template is not null
                    ? template.Evaluate(new DestinationTemplateContext(item.Name, item.IsDirectory, item.LastWriteTime, destinationPathSeparators!))
                    : destinationPattern.HasWildcards
                    ? CombineProviderPath(
                        source.SourceId,
                        string.IsNullOrEmpty(destinationPattern.ParentPath)
                            ? GetProviderParentPath(normalizedSourcePath)
                            : destinationPattern.ParentPath,
                        destinationPattern.TransformName(item.Name))
                    : ResolveProviderMoveTargetPath(source, item, normalizedSourcePath, destinationPath, sources.Count, cancellationToken);

                if (!hasMask)
                {
                    ValidateProviderMoveTarget(source, normalizedSourcePath, targetPath, item.IsDirectory);
                    plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, item, targetPath, source.SourceId));
                    continue;
                }

                FileCopyPlanBuilder.AddMaskedProviderPlanItems(
                    plan, location, source, normalizedSourcePath, item, targetPath, options.FileMask!, matcher, groups,
                    source.SourceId,
                    validateTarget: path => ValidateProviderMoveTarget(source, normalizedSourcePath, path, item.IsDirectory),
                    cancellationToken);
            }

            if (template is not null || destinationPattern.HasWildcards)
                owner.ValidateProviderPlanCollisions(source.SourceId, plan.Select(item => item.TargetPath));
            return plan;
        }

        private static void ValidateProviderMoveTarget(
            IFilePanelSource source,
            string sourcePath,
            string targetPath,
            bool sourceIsDirectory)
        {
            if (ProviderPathRelations.PathsEqual(source, sourcePath, targetPath))
                throw new IOException("Source and destination refer to the same provider item.");

            if (sourceIsDirectory && ProviderPathRelations.IsDescendantOf(source, targetPath, sourcePath))
                throw new IOException("Cannot move a directory into itself.");
        }

        private static string ResolveProviderMoveTargetPath(
            IFilePanelSource source,
            FilePanelItem item,
            string sourcePath,
            string destinationPath,
            int sourceCount,
            CancellationToken cancellationToken)
        {
            if (sourceCount > 1)
                return CombineProviderPath(source.SourceId, destinationPath, item.Name);

            FilePanelItem? destinationItem = source.GetItem(destinationPath, cancellationToken);
            if (destinationItem?.IsDirectory == true)
                return CombineProviderPath(source.SourceId, destinationPath, item.Name);

            return destinationPath;
        }

        private static FileOperationConflict BuildProviderConflict(
            FilePanelItem sourceItem,
            FilePanelItem destinationItem,
            string destinationPath) =>
            new()
            {
                SourcePath = sourceItem.SourcePath,
                DestinationPath = destinationPath,
                SourceIsDirectory = sourceItem.IsDirectory,
                DestinationIsDirectory = destinationItem.IsDirectory,
                SourceSize = sourceItem.Size,
                DestinationSize = destinationItem.Size,
                SourceLastWriteTime = sourceItem.LastWriteTime,
                DestinationLastWriteTime = destinationItem.LastWriteTime,
            };

        private static void EnsureMoveTypeCompatibility(FilePanelItem sourceItem, FilePanelItem destinationItem)
        {
            if (sourceItem.IsDirectory != destinationItem.IsDirectory)
                throw new IOException("Directory/file type conflicts are not overwritten silently.");
        }

        private static void ValidateLocalPlanCollisions(IEnumerable<string> targetPaths) =>
            FileOperationPlanPreflight.ValidateDistinctTargets(
                targetPaths, NormalizePath, LocalFileSystemPathComparer.Current, ValidateLocalTargetPath);

        private static void ValidateLocalMovePlan(IEnumerable<LocalMovePlanItem> plan, bool validateCollisions)
        {
            if (validateCollisions)
                ValidateLocalPlanCollisions(plan.Select(item => item.TargetPath));

            foreach (var item in plan)
            {
                if (validateCollisions && PathsEqual(item.SourcePath, item.TargetPath))
                    throw new IOException("Source and destination file are the same.");
                if (Directory.Exists(item.SourcePath) && IsPathInside(item.TargetPath, item.SourcePath))
                    throw new IOException("Cannot move a directory into itself.");
            }
        }

        private static void ValidateLocalTargetPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            string[] components = fullPath[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            if (components.Length == 0 || components.Any(component =>
                    string.IsNullOrWhiteSpace(component) ||
                    component.IndexOfAny(['*', '?']) >= 0 ||
                    component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                throw new IOException("Destination pattern produced an invalid file name.");
            }
        }

        private sealed record LocalMovePlanItem(FileOperationPlanItem Mapping)
        {
            public LocalMovePlanItem(string source, string target)
                : this(new FileOperationPlanItem(
                    PanelLocation.Local(source), PanelLocation.Local(target), Directory.Exists(source))) { }

            public string SourcePath => Mapping.Source.SourcePath;
            public string TargetPath => Mapping.Destination.SourcePath;
        }

    }
}
