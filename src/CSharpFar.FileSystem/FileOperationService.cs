using System.Diagnostics;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

internal enum DirectMoveResult
{
    Moved,
    NotSupported,
}

internal sealed record FileOperationServiceDependencies
{
    private static readonly CopyResumeAnalyzer ResumeAnalyzer = new();

    public static FileOperationServiceDependencies Default { get; } = new();

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

    public Func<string, FileMode, FileAccess, FileShare, int, FileOptions, Stream> OpenFileStream { get; init; } =
        static (path, mode, access, share, bufferSize, options) =>
            new FileStream(path, mode, access, share, bufferSize, options);

    public Func<string, string, CopyResumeSourceSnapshot?, CancellationToken, CopyResumePlan> AnalyzeResume { get; init; } =
        static (sourcePath, destinationPath, sourceSnapshot, cancellationToken) =>
            ResumeAnalyzer.Analyze(sourcePath, destinationPath, sourceSnapshot, cancellationToken);

    internal Func<string, string, bool> ForceMoveFallback { get; init; } = static (_, _) => false;

    internal Action<string, string> MoveFile { get; init; } =
        static (source, destination) => File.Move(source, destination);

    internal Action<string, string> MoveDirectory { get; init; } =
        static (source, destination) => Directory.Move(source, destination);

    internal Action<string> BeforeDeletePath { get; init; } = static _ => { };
    internal Func<string, string, string?> PublishLocalFile { get; init; } = SafeLocalFilePublisher.Publish;
    internal Action<string, string> PublishNewLocalFile { get; init; } = SafeLocalFilePublisher.MoveStagingPath;
}

public sealed partial class FileOperationService : IFileOperationService, IFileOperationPlanBuilder
{
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly char[] LocalPathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly TimeSpan ReliableCopyRetryDelay = TimeSpan.FromSeconds(1);
    private const int ReliableCopyRetryCount = 20;
    private readonly IFilePanelSourceRegistry _sources;
    private readonly FileOperationServiceDependencies _dependencies;
    private readonly IFileSystemPlatformOperations _platformOperations;

    public bool SupportsRecycleBin => _platformOperations.SupportsRecycleBin;

    public FileOperationService() : this(null, FileOperationServiceDependencies.Default)
    {
    }

    public FileOperationService(IFilePanelSourceRegistry sources) : this(sources, new DefaultFileSystemPlatformOperations(), FileOperationServiceDependencies.Default)
    {
    }

    public FileOperationService(IFilePanelSourceRegistry sources, IFileSystemPlatformOperations platformOperations)
        : this(sources, platformOperations, FileOperationServiceDependencies.Default)
    {
    }

    internal FileOperationService(FileOperationServiceDependencies dependencies) : this(null, dependencies)
    {
    }

    internal FileOperationService(
        IFilePanelSourceRegistry? sources,
        FileOperationServiceDependencies dependencies)
        : this(sources, new DefaultFileSystemPlatformOperations(), dependencies)
    {
    }

    internal FileOperationService(
        IFilePanelSourceRegistry? sources,
        IFileSystemPlatformOperations platformOperations,
        FileOperationServiceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(platformOperations);

        _sources = sources ?? new FilePanelSourceRegistry([new LocalFilePanelSource(new FileSystemService())]);
        _platformOperations = platformOperations;
        _dependencies = dependencies;
    }

    public async Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        IFileOperationConflictResolver conflictResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(conflictResolver);
        using var sourceUsage = (_sources as FilePanelSourceRegistry)?.AcquireUsage(
            request.Sources.Select(location => location.SourceId)
                .Append(request.Destination?.SourceId ?? PanelSourceId.Local).ToArray());

        var state = new OperationState(request.Kind, progress, request.PauseController);

        try
        {
            ValidateAccessibleSources(request);

            if (UsesProviderLocations(request))
            {
                await ExecuteProviderOperationAsync(request, conflictResolver, state, cancellationToken)
                    .ConfigureAwait(false);
                return state.ToResult();
            }

            switch (request.Kind)
            {
                case FileOperationKind.Copy:
                    await CopyProviderAsync(request, conflictResolver, state, cancellationToken,
                        useLocalStrategy: true).ConfigureAwait(false);
                    break;
                case FileOperationKind.Move:
                    await new MoveExecutor(this).MoveAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                    break;
                case FileOperationKind.Delete:
                    new DeleteExecutor(this).ExecuteLocal(request, state, cancellationToken);
                    break;
                case FileOperationKind.CreateDirectory:
                    CreateDirectory(request, state, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported file operation.");
            }
        }
        catch (OperationCanceledException error)
        {
            state.Cancelled = true;
            if (error.Message.StartsWith("Partial data retained", StringComparison.Ordinal))
                state.AddWarning(request.Destination?.SourcePath ?? string.Empty, error.Message);
        }
        catch (UnknownFilePublicationException error)
        {
            state.PublicationUnknown = true;
            state.AddError(error.DestinationPath, error.Message);
        }

        return state.ToResult();
    }

    public FileOperationPlan BuildPlan(FileOperationRequest request, CancellationToken cancellationToken = default) =>
        BuildPlanAsync(request, cancellationToken).GetAwaiter().GetResult();

    public Task<FileOperationPlan> BuildPlanAsync(FileOperationRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildPlanCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<FileOperationPlan> BuildPlanCoreAsync(FileOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var sourceUsage = (_sources as FilePanelSourceRegistry)?.AcquireUsage(
            request.Sources.Select(location => location.SourceId)
                .Append(request.Destination?.SourceId ?? PanelSourceId.Local).ToArray());
        ValidateAccessibleSources(request);

        if (UsesProviderLocations(request))
            return await BuildProviderPlanAsync(request, cancellationToken).ConfigureAwait(false);

        return request.Kind switch
        {
            FileOperationKind.Copy => await BuildLocalCopyPlanAsync(request, cancellationToken).ConfigureAwait(false),
            FileOperationKind.Move => BuildLocalMoveOperationPlan(request, cancellationToken),
            _ => throw new InvalidOperationException("Preview is available only for copy, move, and rename operations."),
        };
    }

    private async Task<FileOperationPlan> BuildLocalCopyPlanAsync(FileOperationRequest request, CancellationToken cancellationToken) =>
        ToPublicPlan(
            await BuildProviderCopyPlanAsync(
                RequireSourceLocations(request),
                RequireDestinationLocation(request),
                request.Options,
                request.UseDestinationTemplate,
                cancellationToken).ConfigureAwait(false),
            PanelSourceId.Local);

    private FileOperationPlan BuildLocalMoveOperationPlan(FileOperationRequest request, CancellationToken cancellationToken) =>
        new MoveExecutor(this).BuildLocalMovePreviewPlan(request, cancellationToken);

    internal static bool IsKnownCrossFileSystemMove(string source, string destination) =>
        MoveExecutor.IsKnownCrossFileSystemMove(source, destination);

    private async Task<FileOperationPlan> BuildProviderPlanAsync(FileOperationRequest request, CancellationToken cancellationToken)
    {
        return request.Kind switch
        {
            FileOperationKind.Copy => ToPublicPlan(await BuildProviderCopyPlanAsync(RequireSourceLocations(request), RequireDestinationLocation(request), request.Options, request.UseDestinationTemplate, cancellationToken).ConfigureAwait(false), RequireDestinationLocation(request).SourceId),
            FileOperationKind.Move => await BuildProviderMovePreviewPlanAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Preview is available only for copy, move, and rename operations."),
        };
    }

    private async Task<FileOperationPlan> BuildProviderMovePreviewPlanAsync(
        FileOperationRequest request, CancellationToken cancellationToken)
    {
        var sources = RequireSourceLocations(request);
        var destination = RequireDestinationLocation(request);
        var source = _sources.GetSource(sources[0].SourceId);
        var target = _sources.GetSource(destination.SourceId);
        var plan = source.SourceId == target.SourceId || SamePhysicalContainer(source, target)
            ? await new MoveExecutor(this).BuildProviderMovePlanAsync(source, sources, destination.SourcePath,
                request.Options, request.UseDestinationTemplate, cancellationToken).ConfigureAwait(false)
            : await BuildProviderCopyPlanAsync(sources, destination, request.Options,
                request.UseDestinationTemplate, cancellationToken).ConfigureAwait(false);
        return ToPublicPlan(plan, destination.SourceId);
    }

    private static FileOperationPlan ToPublicPlan(IEnumerable<ProviderOperationPlanItem> plan, PanelSourceId destinationSourceId) =>
        new(FlattenProviderPlan(plan).Where(item => item.Item is not null).Select(item =>
            item.Mapping.Destination.SourceId == destinationSourceId
                ? item.Mapping
                : item.Mapping with { Destination = new PanelLocation(destinationSourceId, item.TargetPath) }).ToArray());

    private static IEnumerable<ProviderOperationPlanItem> FlattenProviderPlan(IEnumerable<ProviderOperationPlanItem> roots)
    {
        foreach (ProviderOperationPlanItem root in roots)
        {
            yield return root;
            foreach (ProviderOperationPlanItem child in FlattenProviderPlan(root.Children))
                yield return child;
        }
    }

    private static int CountProviderPlanItems(IEnumerable<ProviderOperationPlanItem> roots) =>
        FlattenProviderPlan(roots).Count();

    private static bool UsesProviderLocations(FileOperationRequest request) =>
        request.Sources.Any(location => location.SourceId != PanelSourceId.Local) ||
        request.Destination is { SourceId: var id } && id != PanelSourceId.Local;

    private void ValidateAccessibleSources(FileOperationRequest request)
    {
        foreach (PanelSourceId sourceId in EnumerateReferencedSources(request).Distinct())
        {
            if (_sources.TryGetSource(sourceId, out _))
                continue;

            throw new IOException($"Panel source '{sourceId.Value}' is not available in the current composition.");
        }
    }

    private async Task ExecuteProviderOperationAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        switch (request.Kind)
        {
            case FileOperationKind.Copy:
                await CopyProviderAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.Move:
                await new MoveExecutor(this).MoveProviderAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.Delete:
                await new DeleteExecutor(this).ExecuteProviderAsync(request, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.CreateDirectory:
                await CreateProviderDirectoryAsync(request, state, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported file operation.");
        }
    }

    private async Task<CopyOutcome> CopyProviderAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken,
        bool useLocalStrategy = false,
        string? rootTarget = null)
    {
        if (!useLocalStrategy)
            ValidateProviderCopyOptions(request.Options);

        var destination = RequireDestinationLocation(request);
        var sources = RequireSourceLocations(request);
        var destinationSource = _sources.GetSource(destination.SourceId);
        var plan = await BuildProviderCopyPlanAsync(sources, destination, request.Options, request.UseDestinationTemplate, cancellationToken, state, rootTarget).ConfigureAwait(false);
        var copiedFiles = new List<string>();
        var copiedFolders = new List<string>();
        int skippedBefore = state.SkippedCount;
        int errorsBefore = state.Errors.Count;

        state.SetTotals(CalculateProviderPlanSize(plan), CountProviderPlanItems(plan));
        state.StartProgressTimer();

        if (destinationSource is IFileOperationBatchProvider batching)
        {
            var publicPlan = ToPublicPlan(plan, destination.SourceId);
            int before = state.CopiedCount;
            try
            {
                string? warning = await SafeFileWriteService.ExecuteBatchAsync(batching, publicPlan, async batch =>
                {
                    await CopyProviderPlanItemsAsync(plan, batch, request.Options,
                        conflictResolver, state, cancellationToken,
                        copiedFiles, copiedFolders, useLocalStrategy: useLocalStrategy).ConfigureAwait(false);
                    state.ReportCommitting(destination.SourcePath);
                }, cancellationToken).ConfigureAwait(false);
                if (warning is not null) state.AddWarning(destination.SourcePath, warning);
            }
            catch
            {
                state.CopiedCount = before;
                throw;
            }
        }
        else
        {
            await CopyProviderPlanItemsAsync(plan, destinationSource, request.Options,
                conflictResolver, state, cancellationToken,
                copiedFiles, copiedFolders, useLocalStrategy: useLocalStrategy).ConfigureAwait(false);
        }

        return new CopyOutcome(
            copiedFiles.Count > 0 || copiedFolders.Count > 0,
            state.SkippedCount == skippedBefore && state.Errors.Count == errorsBefore,
            copiedFiles);
    }

    private async Task CopyProviderPlanItemsAsync(
        IReadOnlyList<ProviderOperationPlanItem> plan,
        IFilePanelSource destinationSource,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken,
        ICollection<string>? copiedFiles = null,
        ICollection<string>? copiedFolders = null,
        bool useLocalStrategy = false)
    {
        foreach (var plannedItem in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plannedItem.Item is null)
            {
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            await CopyProviderItemAsync(
                plannedItem, destinationSource,
                options, conflictResolver, state, cancellationToken, copiedFiles, copiedFolders,
                useLocalStrategy).ConfigureAwait(false);
        }
    }

    private async Task CopyProviderItemAsync(
        ProviderOperationPlanItem plannedItem,
        IFilePanelSource destinationSource,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken,
        ICollection<string>? copiedFiles = null,
        ICollection<string>? copiedFolders = null,
        bool useLocalStrategy = false)
    {
        IFilePanelSource source = plannedItem.Source;
        string sourcePath = plannedItem.SourcePath;
        string destinationPath = plannedItem.TargetPath;
        FilePanelItem sourceItem = plannedItem.Item ?? throw new InvalidOperationException("Missing planned source item.");
        bool isLink = (sourceItem.Attributes & FileAttributes.ReparsePoint) != 0;
        if (isLink && source.SourceId != PanelSourceId.Local)
            throw new NotSupportedException($"Symbolic links are unsupported for source provider '{source.DisplayName}'.");
        if (sourceItem.IsDirectory && !(isLink && options.SymlinkMode == SymlinkCopyMode.CopyLink))
        {
            if (source.SourceId == destinationSource.SourceId ||
                SamePhysicalContainer(source, destinationSource))
                FileCopyPlanBuilder.ValidateCopyTarget(source, sourcePath, destinationPath, sourceItem.IsDirectory);
            await EnsureProviderDestinationDirectoryAsync(destinationSource, destinationPath, cancellationToken).ConfigureAwait(false);
            await destinationSource.AsAsync().CreateDirectoryAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            state.CompleteFolder();

            foreach (ProviderOperationPlanItem child in plannedItem.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyProviderItemAsync(
                        child,
                        destinationSource,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken,
                        copiedFiles,
                        copiedFolders,
                        useLocalStrategy)
                    .ConfigureAwait(false);
            }

            if (useLocalStrategy)
                PreserveDirectoryTargetsMetadata(
                    [new CopyDirectoryPlanItem(sourcePath, destinationPath)],
                    options, state, cancellationToken);

            copiedFolders?.Add(sourcePath);
            state.CompleteItem();
            return;
        }

        state.WaitIfPaused(cancellationToken);
        if (useLocalStrategy)
        {
            if (await CopyLocalPlannedFileAsync(
                    plannedItem, options, conflictResolver, state, cancellationToken)
                .ConfigureAwait(false))
                copiedFiles?.Add(sourcePath);
            return;
        }
        if ((source.SourceId == destinationSource.SourceId ||
            SamePhysicalContainer(source, destinationSource)) &&
            ProviderPathRelations.PathsEqual(source, sourcePath, destinationPath))
            throw new IOException("Cannot copy an archive entry onto itself.");
        if (isLink && options.SymlinkMode == SymlinkCopyMode.CopyLink)
            throw new NotSupportedException("The destination provider cannot create symbolic links safely.");
        FilePanelItem? approvedDestination = null;
        while (await destinationSource.AsAsync().GetItemAsync(destinationPath, cancellationToken).ConfigureAwait(false) is { } destinationItem)
        {
            var decision = state.Conflicts.Resolve(
                options,
                conflictResolver,
                () => new FileOperationConflict
                {
                    SourcePath = sourcePath,
                    DestinationPath = destinationPath,
                    SourceIsDirectory = false,
                    DestinationIsDirectory = destinationItem.IsDirectory,
                    SourceSize = sourceItem.Size,
                    DestinationSize = destinationItem.Size,
                    SourceLastWriteTime = sourceItem.LastWriteTime,
                    DestinationLastWriteTime = destinationItem.LastWriteTime,
                });

            switch (decision.Mode)
            {
                case ConflictDecisionMode.Skip:
                    state.SkippedCount++;
                    state.CompleteItem();
                    return;
                case ConflictDecisionMode.Rename:
                    destinationPath = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                        ? await GenerateProviderNameAsync(destinationSource, destinationPath, cancellationToken).ConfigureAwait(false)
                        : decision.NewDestinationPath;
                    if (source.SourceId == destinationSource.SourceId ||
                        SamePhysicalContainer(source, destinationSource))
                        FileCopyPlanBuilder.ValidateCopyTarget(source, sourcePath, destinationPath, sourceItem.IsDirectory);
                    continue;
                case ConflictDecisionMode.Cancel:
                    throw new OperationCanceledException("File operation cancelled by user.");
                case ConflictDecisionMode.Overwrite:
                    if (destinationItem.IsDirectory)
                        throw new IOException("Cannot overwrite a directory with a file.");
                    if (destinationSource is not IFileOperationBatch && destinationSource.SourceId != PanelSourceId.Local)
                        throw new NotSupportedException($"Provider '{destinationSource.DisplayName}' cannot safely replace an existing file without transactional batch support.");
                    approvedDestination = destinationItem;
                    break;
                case ConflictDecisionMode.OnlyNewer:
                    throw new InvalidOperationException("Only newer is not supported for provider copy.");
                default:
                    throw new InvalidOperationException($"Unsupported copy conflict decision: {decision.Mode}.");
            }
            break;
        }

        await EnsureProviderDestinationDirectoryAsync(destinationSource, destinationPath, cancellationToken).ConfigureAwait(false);
        string? warning = await SafeFileWriteService.WriteProviderAsync(
            destinationSource, destinationPath, approvedDestination, sourceItem.Size,
            async output =>
            {
                long done = 0;
                await using Stream input = await source.AsAsync().OpenReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                byte[] buffer = new byte[CopyBufferSize];
                while (true)
                {
                    state.WaitIfPaused(cancellationToken);
                    int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    state.AddBytes(read);
                    state.Report(sourcePath, destinationPath, done, sourceItem.Size ?? 0);
                }
                return done;
            }, options.KeepPartialFilesOnError, cancellationToken, _dependencies.PublishLocalFile, _dependencies.PublishNewLocalFile).ConfigureAwait(false);
        if (warning is not null) state.AddWarning(destinationPath, warning);

        copiedFiles?.Add(sourcePath);
        state.CopiedCount++;
        state.CompleteItem();
    }

    private async Task<bool> CopyLocalPlannedFileAsync(
        ProviderOperationPlanItem item,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string sourcePath = item.SourcePath;
        bool copyLink = (item.Item!.Attributes & FileAttributes.ReparsePoint) != 0 &&
                        options.SymlinkMode == SymlinkCopyMode.CopyLink;
        CopyResumeSourceSnapshot snapshot = copyLink
            ? new CopyResumeSourceSnapshot(0, item.Item.LastWriteTime.ToUniversalTime())
            : GetSourceSnapshot(sourcePath, followLink: (item.Item.Attributes & FileAttributes.ReparsePoint) != 0);
        var file = new CopyFilePlanItem(
            sourcePath, item.TargetPath, snapshot.Length, snapshot.LastWriteTimeUtc);
        CopyDestination target = CopyDestination.Skip;
        try
        {
            target = await ResolveDestinationPathAsync(
                file, options, conflictResolver, state, cancellationToken).ConfigureAwait(false);
            if (target.Path.Length == 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
            if (copyLink)
            {
                if (!CopyReparsePoint(sourcePath, target, options, state))
                    return false;
                state.CopiedCount++;
                state.AddBytes(file.Size);
                state.CompleteItem();
                state.Report(sourcePath, target.Path, file.Size, file.Size);
                return true;
            }

            return await CopyFileContentsAsync(
                file, target, options, conflictResolver, state, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CopyRetryableIOException ex) when (
            options.CopyMode == CopyMode.FastSalvage && ex.FailureKind == "Read failed")
        {
            string partialPath = target.Path.Length == 0 ? file.DestinationPath : target.Path;
            // The staging file is cleaned by CopyFileContentsAsync, never the user destination.
            state.AddError(file.SourcePath, $"Copy to '{partialPath}' failed: {ex.Message}");
            state.SkippedCount++;
            state.CompleteItem();
            state.ReportValidation(
                file.SourcePath,
                partialPath,
                file.Size,
                "Read failed, skipping file...",
                resumeOffset: null,
                rollbackBytes: null);
            return false;
        }
    }

    private static bool SamePhysicalContainer(IFilePanelSource first, IFilePanelSource second) =>
        first is IFileProviderPhysicalIdentity a &&
        second is IFileProviderPhysicalIdentity b &&
        string.Equals(a.PhysicalIdentity, b.PhysicalIdentity,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task CreateProviderDirectoryAsync(
        FileOperationRequest request,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var destination = RequireDestinationLocation(request);
        var source = _sources.GetSource(destination.SourceId);
        if (source is IFileOperationBatchProvider batching)
        {
            var location = new PanelLocation(destination.SourceId, destination.SourcePath);
            var plan = new FileOperationPlan([new FileOperationPlanItem(location, location, true)]);
            await using var batch = await batching.BeginBatchAsync(plan, cancellationToken).ConfigureAwait(false);
            try
            {
                await batch.CreateDirectoryAsync(destination.SourcePath, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                state.ReportCommitting(destination.SourcePath);
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
            await source.AsAsync().CreateDirectoryAsync(destination.SourcePath, cancellationToken).ConfigureAwait(false);
        }
        state.CreatedDirectoryCount++;
        state.SetTotals(0, 1);
        state.CompleteItem();
    }

    // Move fallbacks and staged replacements reuse the same tree executor as F5 copy.
    private static void CreateDirectory(FileOperationRequest request, OperationState state, CancellationToken cancellationToken)
    {
        string path = RequireDestination(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(path))
            throw new IOException($"Folder '{Path.GetFileName(path)}' already exists.");

        Directory.CreateDirectory(path);
        state.CreatedDirectoryCount++;
        state.SetTotals(0, 1);
        state.CompleteItem();
    }

    private async Task<CopyDestination> ResolveDestinationPathAsync(
        CopyFilePlanItem file,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string destinationPath = file.DestinationPath;
        while (SafeLocalFilePublisher.CaptureDestination(destinationPath) is { } snapshot)
        {
            ConflictDecisionMode mode = state.Conflicts.Current(options.DefaultConflictDecision);
            string? newDestination = null;

            FileOperationConflictDecision decision = state.Conflicts.Resolve(
                options, conflictResolver, () => BuildConflict(file.SourcePath, destinationPath));
            mode = decision.Mode;
            newDestination = decision.NewDestinationPath;

            switch (mode)
            {
                case ConflictDecisionMode.Overwrite:
                    if (Directory.Exists(destinationPath))
                        throw new IOException("Cannot overwrite a directory with a file.");
                    return new CopyDestination(destinationPath, CopyDestinationAction.CreateOrOverwrite, ExpectedDestination: snapshot);
                case ConflictDecisionMode.Skip:
                    state.SkippedCount++;
                    state.CompleteItem();
                    return CopyDestination.Skip;
                case ConflictDecisionMode.Rename:
                    destinationPath = string.IsNullOrWhiteSpace(newDestination)
                        ? GenerateName(destinationPath)
                        : newDestination;
                    continue;
                case ConflictDecisionMode.OnlyNewer:
                    if (File.Exists(destinationPath) && IsSourceNewer(file.SourcePath, destinationPath))
                    {
                        return new CopyDestination(destinationPath, CopyDestinationAction.CreateOrOverwrite, ExpectedDestination: snapshot);
                    }
                    state.SkippedCount++;
                    state.CompleteItem();
                    return CopyDestination.Skip;
                case ConflictDecisionMode.Cancel:
                    throw new OperationCanceledException("File operation cancelled by user.");
                case ConflictDecisionMode.Ask:
                    continue;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported conflict decision.");
            }
        }

        return new CopyDestination(destinationPath, CopyDestinationAction.CreateOrOverwrite);
    }

    private CopyDestination? TryResolveTailValidatedResume(
        CopyFilePlanItem file,
        string destinationPath,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken,
        out CopyResumePlan? sourceReadFailure)
    {
        sourceReadFailure = null;

        if (!File.Exists(destinationPath) || Directory.Exists(destinationPath))
            return null;

        if (IsReparsePoint(file.SourcePath) && options.SymlinkMode == SymlinkCopyMode.CopyLink)
            return null;

        long destinationLength;
        try
        {
            destinationLength = new FileInfo(destinationPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (destinationLength >= file.Size)
            return null;

        state.ReportValidation(
            file.SourcePath,
            destinationPath,
            file.Size,
            "Validating partial file...",
            resumeOffset: null,
            rollbackBytes: null);

        var snapshot = new CopyResumeSourceSnapshot(file.Size, file.LastWriteTimeUtc);
        CopyResumePlan plan = _dependencies.AnalyzeResume(file.SourcePath, destinationPath, snapshot, cancellationToken);
        if (plan.Kind != CopyResumePlanKind.CanResume)
        {
            if (plan.ReadFailureSide == CopyResumeReadFailureSide.Source)
            {
                sourceReadFailure = plan;
                return null;
            }

            state.ReportValidation(
                file.SourcePath,
                destinationPath,
                file.Size,
                "Tail mismatch detected",
                resumeOffset: null,
                rollbackBytes: null);
            return null;
        }

        string status = plan.RollbackBytes > 0
            ? "Tail mismatch detected"
            : "Tail validation passed";
        state.ReportValidation(
            file.SourcePath,
            destinationPath,
            file.Size,
            status,
            plan.SafeResumeOffset,
            plan.RollbackBytes);

        return new CopyDestination(
            destinationPath,
            CopyDestinationAction.ResumeWithTailValidation,
            plan.SafeResumeOffset,
            plan.RollbackBytes);
    }

    private async Task WaitForReliableRetryAsync(
        CopyFilePlanItem file,
        string destinationPath,
        OperationState state,
        string failureKind,
        string reason,
        CancellationToken cancellationToken)
    {
        string status = string.IsNullOrWhiteSpace(reason)
            ? $"{failureKind}; retrying..."
            : $"{failureKind}; retrying. {reason}";
        state.ReportValidation(
            file.SourcePath,
            destinationPath,
            file.Size,
            status,
            resumeOffset: null,
            rollbackBytes: null);

        await _dependencies.DelayAsync(ReliableCopyRetryDelay, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CopyFileContentsAsync(
        CopyFilePlanItem file,
        CopyDestination initialTarget,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        bool retry = options.CopyMode == CopyMode.Reliable;
        bool wrap = retry || options.CopyMode == CopyMode.FastSalvage;
        string destination = initialTarget.Path;
        string staging = SafeLocalFilePublisher.CreateStagingPath(destination);
        CopyDestination target = new(staging, CopyDestinationAction.CreateOrOverwrite);
        long accounted = 0;
        int attempts = 0;
        state.Report(file.SourcePath, destination, 0, file.Size);
        bool owned = false;
        try
        {
            // Reserve this path exclusively before any retry can truncate it.
            using (var reservation = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                owned = true;
            while (true)
            {
                try
                {
                    await CopyFileAttemptAsync(file, target, CurrentBytesForTarget(target),
                        wrap, Account, state, destination, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (CopyRetryableIOException ex) when (retry && attempts < ReliableCopyRetryCount)
                {
                    attempts++;
                    await WaitForReliableRetryAsync(file, destination, state,
                        ex.FailureKind, ex.InnerException?.Message ?? ex.Message, cancellationToken)
                        .ConfigureAwait(false);
                    // Only an operation-owned staging file may be resumed.
                    target = TryResolveTailValidatedResume(file, staging, options, state,
                        cancellationToken, out _) ??
                        new CopyDestination(staging, CopyDestinationAction.CreateOrOverwrite);
                    Account(CurrentBytesForTarget(target));
                    state.Report(file.SourcePath, destination, CurrentBytesForTarget(target), file.Size);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(staging).Length != file.Size)
                throw new IOException($"Incomplete copy from '{file.SourcePath}' to '{destination}'.");
            _platformOperations.PreserveFileMetadata(file.SourcePath, staging, options, state);
            cancellationToken.ThrowIfCancellationRequested();
            string? leftover = SafeLocalFilePublisher.PublishChecked(staging, destination, initialTarget.ExpectedDestination, _dependencies.PublishLocalFile, _dependencies.PublishNewLocalFile);
            if (leftover is not null)
                state.AddWarning(destination, $"Backup cleanup failed after publishing '{destination}'. Backup: {leftover}");
            state.CopiedCount++;
            state.CompleteItem();
            return true;
        }
        catch (Exception error)
        {
            if (owned)
            {
                try { CleanupPartialDestination(staging, options, state); }
                catch (Exception cleanup)
                {
                    throw new IOException($"Copy failed and staging cleanup failed: '{staging}'.",
                        new AggregateException(error, cleanup));
                }
            }
            throw;
        }

        void Account(long current)
        {
            long delta = current - accounted;
            if (delta != 0) state.AddBytes(delta);
            accounted = current;
        }
    }

    private async Task<CopyAttemptResult> CopyFileAttemptAsync(
        CopyFilePlanItem file,
        CopyDestination copyTarget,
        long startOffset,
        bool wrapSourceReadFailures,
        Action<long> accountCurrentBytes,
        OperationState state,
        string publishedDestination,
        CancellationToken cancellationToken)
    {
        FileInfo sourceInfo;
        FileAttributes sourceAttributes;
        try
        {
            sourceInfo = new FileInfo(file.SourcePath);
            sourceAttributes = sourceInfo.Attributes;
        }
        catch (Exception ex) when (wrapSourceReadFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Read failed", ex);
        }

        await using Stream source = OpenFileStream(
            file.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan,
            wrapSourceReadFailures);

        await using Stream destination = OpenDestinationStream(
            copyTarget.Path,
            copyTarget.Action == CopyDestinationAction.ResumeWithTailValidation
                ? FileMode.Open
                : FileMode.Create,
            copyTarget.Action == CopyDestinationAction.ResumeWithTailValidation
                ? FileAccess.ReadWrite
                : FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous | FileOptions.SequentialScan,
            wrapSourceReadFailures);

        if (copyTarget.Action == CopyDestinationAction.ResumeWithTailValidation)
        {
            destination.SetLength(copyTarget.ResumeOffset);
            SeekSource(source, copyTarget.ResumeOffset, wrapSourceReadFailures);
            destination.Seek(copyTarget.ResumeOffset, SeekOrigin.Begin);
        }

        byte[] buffer = new byte[CopyBufferSize];
        long currentBytes = startOffset;
        while (true)
        {
            state.WaitIfPaused(cancellationToken);
            int read = await ReadFromSourceAsync(source, buffer, wrapSourceReadFailures, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            await WriteToDestinationAsync(destination, buffer, read, wrapSourceReadFailures, cancellationToken)
                .ConfigureAwait(false);
            currentBytes += read;
            accountCurrentBytes(currentBytes);
            state.Report(file.SourcePath, publishedDestination, currentBytes, file.Size);
        }

        try { await destination.FlushAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (wrapSourceReadFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Write failed", ex);
        }
        return new CopyAttemptResult(sourceInfo, sourceAttributes);
    }

    private Stream OpenFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options,
        bool wrapSourceReadFailures)
    {
        try
        {
            return _dependencies.OpenFileStream(path, mode, access, share, CopyBufferSize, options);
        }
        catch (Exception ex) when (wrapSourceReadFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Read failed", ex);
        }
    }

    private Stream OpenDestinationStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options,
        bool wrapFailures)
    {
        try
        {
            return _dependencies.OpenFileStream(path, mode, access, share, CopyBufferSize, options);
        }
        catch (Exception ex) when (wrapFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Write failed", ex);
        }
    }

    private static async Task<int> ReadFromSourceAsync(
        Stream source,
        byte[] buffer,
        bool wrapSourceReadFailures,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (wrapSourceReadFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Read failed", ex);
        }
    }

    private static async Task WriteToDestinationAsync(
        Stream destination,
        byte[] buffer,
        int count,
        bool wrapFailures,
        CancellationToken cancellationToken)
    {
        try
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (wrapFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Write failed", ex);
        }
    }

    private static void SeekSource(Stream source, long offset, bool wrapSourceReadFailures)
    {
        try
        {
            source.Seek(offset, SeekOrigin.Begin);
        }
        catch (Exception ex) when (wrapSourceReadFailures && IsFileAccessException(ex))
        {
            throw new CopyRetryableIOException("Read failed", ex);
        }
    }

    private static long CurrentBytesForTarget(CopyDestination copyTarget) =>
        copyTarget.Action == CopyDestinationAction.ResumeWithTailValidation
            ? copyTarget.ResumeOffset
            : 0;

    private void PreserveDirectoryTargetsMetadata(
        IReadOnlyList<CopyDirectoryPlanItem> directories,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken)
    {
        for (int i = directories.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories[i];
            _platformOperations.PreserveFileMetadata(directory.SourcePath, directory.DestinationPath, options, state);
        }
    }

    private bool CopyReparsePoint(
        string sourcePath,
        CopyDestination target,
        FileOperationOptions options,
        OperationState state)
    {
        string destinationPath = target.Path;
        if (options.SymlinkMode == SymlinkCopyMode.CopyTargetContents)
            return true;

        string staging = SafeLocalFilePublisher.CreateStagingPath(destinationPath);
        bool owned = false;
        try
        {
            if (!_platformOperations.TryCopySymbolicLink(sourcePath, staging, out string? error))
            {
                state.AddError(sourcePath, error ?? "Cannot copy symbolic link.");
                return false;
            }
            owned = true;
            string? leftover = SafeLocalFilePublisher.PublishChecked(staging, destinationPath, target.ExpectedDestination, _dependencies.PublishLocalFile, _dependencies.PublishNewLocalFile);
            if (leftover is not null)
                state.AddWarning(destinationPath, $"Backup cleanup failed. Backup: {leftover}");
            return true;
        }
        finally
        {
            if (owned)
            {
                try { SafeLocalFilePublisher.DeleteStagingPath(staging); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    state.AddWarning(staging, $"Symbolic-link staging cleanup failed: {cleanup.Message}");
                }
            }
        }
    }

    private void DeletePath(string path, bool useRecycleBin) =>
        new DeleteExecutor(this).DeletePath(path, useRecycleBin);

    private bool IsReparsePoint(string path)
    {
        return _platformOperations.IsSymbolicLink(path);
    }

    private static FileOperationConflict BuildConflict(string sourcePath, string destinationPath)
    {
        bool sourceIsDirectory = Directory.Exists(sourcePath);
        bool destinationIsDirectory = Directory.Exists(destinationPath);
        return new FileOperationConflict
        {
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            SourceIsDirectory = sourceIsDirectory,
            DestinationIsDirectory = destinationIsDirectory,
            SourceSize = sourceIsDirectory ? null : GetFileSize(sourcePath),
            DestinationSize = destinationIsDirectory ? null : GetFileSize(destinationPath),
            SourceLastWriteTime = GetLastWriteTime(sourcePath),
            DestinationLastWriteTime = GetLastWriteTime(destinationPath),
        };
    }

    private static long GetFileSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static DateTime? GetLastWriteTime(string path)
    {
        if (File.Exists(path))
            return File.GetLastWriteTime(path);
        if (Directory.Exists(path))
            return Directory.GetLastWriteTime(path);
        return null;
    }

    private static bool IsSourceNewer(string source, string destination) =>
        File.GetLastWriteTime(source) > File.GetLastWriteTime(destination);

    private static void DeleteFileTarget(string path)
    {
        if (!File.Exists(path))
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

        File.Delete(path);
    }

    private static CopyResumeSourceSnapshot GetSourceSnapshot(string path, bool followLink = false)
    {
        var source = new FileInfo(path);
        if (!followLink)
            return new CopyResumeSourceSnapshot(source.Length, source.LastWriteTimeUtc);

        // On Unix, FileInfo.Length may report the byte length of the link itself.
        // The transfer follows the target, so the expected size must come from it.
        using Stream resolvedSource = File.OpenRead(path);
        return new CopyResumeSourceSnapshot(resolvedSource.Length, source.LastWriteTimeUtc);
    }

    private static void ValidateProviderCopyOptions(FileOperationOptions options)
    {
        if (options.CopyMode == CopyMode.Reliable)
            throw new InvalidOperationException("Reliable copy is not supported for provider copy.");
        if (options.CopyMode == CopyMode.FastSalvage)
            throw new InvalidOperationException("Fast salvage copy is not supported for provider copy.");
        if (options.DefaultConflictDecision == ConflictDecisionMode.OnlyNewer)
            throw new InvalidOperationException("Only newer is not supported for provider copy.");
    }

    private static void CleanupPartialDestination(
        string destinationPath,
        FileOperationOptions options,
        OperationState state)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || !SafeLocalFilePublisher.PathExists(destinationPath))
            return;

        if (!options.KeepPartialFilesOnError)
        {
            SafeLocalFilePublisher.DeleteStagingPath(destinationPath);
            state.ReportValidation(
                destinationPath,
                destinationPath,
                0,
                "Partial destination deleted",
                resumeOffset: null,
                rollbackBytes: null);
            return;
        }

        string partialPath = GeneratePartialName(destinationPath);
        SafeLocalFilePublisher.MoveStagingPath(destinationPath, partialPath);
        state.ReportValidation(
            destinationPath,
            partialPath,
            0,
            "Partial destination kept",
            resumeOffset: null,
            rollbackBytes: null);
    }

    private static bool IsFileAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static string GeneratePartialName(string destinationPath)
    {
        string candidate = destinationPath + ".partial";
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        for (int i = 2; ; i++)
        {
            candidate = destinationPath + $".partial.{i}";
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static string GenerateName(string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(destinationPath);
        string extension = Path.GetExtension(destinationPath);

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    internal static bool IsPath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar) ||
        Path.IsPathRooted(value);

    private static string RequireDestination(FileOperationRequest request)
    {
        PanelLocation destination = RequireDestinationLocation(request);
        if (destination.SourceId != PanelSourceId.Local)
            throw new ArgumentException("Expected a local destination.", nameof(request));
        return destination.SourcePath;
    }

    private static IReadOnlyList<string> RequireLocalSourcePaths(FileOperationRequest request)
    {
        if (request.Sources.Any(location => location.SourceId != PanelSourceId.Local))
            throw new ArgumentException("Expected local sources.", nameof(request));
        return request.Sources.Select(location => location.SourcePath).ToArray();
    }

    private static IReadOnlyList<PanelLocation> RequireSourceLocations(FileOperationRequest request) =>
        request.Sources.Count > 0 ? request.Sources :
            throw new ArgumentException("At least one source is required.", nameof(request));

    private static PanelLocation RequireDestinationLocation(FileOperationRequest request) =>
        request.Destination is { } destination && !string.IsNullOrWhiteSpace(destination.SourcePath)
            ? destination : throw new ArgumentException("Destination is required.", nameof(request));

    private Task<List<ProviderOperationPlanItem>> BuildProviderCopyPlanAsync(
        IReadOnlyList<PanelLocation> sources,
        PanelLocation destination,
        FileOperationOptions options,
        bool useDestinationTemplate,
        CancellationToken cancellationToken,
        OperationState? state = null,
        string? rootTarget = null) =>
        new FileCopyPlanBuilder(_sources, ValidateProviderPlanCollisions)
            .BuildAsync(sources, destination, options, useDestinationTemplate,
                cancellationToken, state is null ? null : state.ReportScanning, rootTarget);

    private static long CalculateProviderPlanSize(IEnumerable<ProviderOperationPlanItem> plan) =>
        FlattenProviderPlan(plan).Sum(item => item.Item?.Size ?? 0);

    private static async Task EnsureProviderDestinationDirectoryAsync(
        IFilePanelSource destinationSource,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string? parent = destinationSource.GetParentPath(destinationPath);
        if (string.IsNullOrEmpty(parent) || await destinationSource.AsAsync().GetItemAsync(parent, cancellationToken).ConfigureAwait(false) is not null)
            return;

        await EnsureProviderDestinationDirectoryAsync(destinationSource, parent, cancellationToken).ConfigureAwait(false);
        await destinationSource.AsAsync().CreateDirectoryAsync(parent, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<PanelSourceId> EnumerateReferencedSources(FileOperationRequest request)
    {
        foreach (PanelLocation location in request.Sources)
            yield return location.SourceId;
        if (request.Destination is { } destination)
            yield return destination.SourceId;
    }

    private static async Task<string> GenerateProviderNameAsync(
        IFilePanelSource source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string directory;
        string fileName;
        string extension;

        if (source.SourceId == PanelSourceId.Local)
        {
            directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
            fileName = Path.GetFileNameWithoutExtension(destinationPath);
            extension = Path.GetExtension(destinationPath);
        }
        else
        {
            directory = source.GetParentPath(destinationPath)
                ?? throw new IOException("Destination has no parent directory.");
            string name = source.GetFileName(destinationPath);
            int dot = name.LastIndexOf('.');
            if (dot <= 0)
            {
                fileName = name;
                extension = string.Empty;
            }
            else
            {
                fileName = name[..dot];
                extension = name[dot..];
            }
        }

        for (int i = 2; ; i++)
        {
            string candidate = source.CombinePath(directory, $"{fileName} ({i}){extension}");
            if (await source.AsAsync().GetItemAsync(candidate, cancellationToken).ConfigureAwait(false) is null)
                return candidate;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        LocalFileSystemPathComparer.Current.Equals(NormalizePath(left), NormalizePath(right));

    private static bool IsPathInside(string path, string possibleParent)
    {
        string child = NormalizePath(path);
        string parent = NormalizePath(possibleParent);
        if (!parent.EndsWith(Path.DirectorySeparatorChar))
            parent += Path.DirectorySeparatorChar;

        return child.StartsWith(parent, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void ValidateProviderPlanCollisions(PanelSourceId sourceId, IEnumerable<string> targetPaths)
    {
        IFilePanelSource destinationSource = _sources.GetSource(sourceId);
        FileOperationPlanPreflight.ValidateDistinctTargets(
            targetPaths.Where(path => !string.IsNullOrEmpty(path)),
            destinationSource.NormalizePath,
            destinationSource.PathComparer,
            path =>
            {
                if (path.IndexOfAny(['*', '?']) >= 0)
                    throw new IOException("Destination pattern produced an invalid file name.");
            });
    }

    // The canonical item mapping is shared by Local, provider execution and previews.
    // Wrappers retain only strategy-specific execution metadata.
    private sealed record CopyFilePlanItem(FileOperationPlanItem Mapping, long Size, DateTime LastWriteTimeUtc)
    {
        public CopyFilePlanItem(string source, string target, long size, DateTime lastWriteTimeUtc)
            : this(new FileOperationPlanItem(PanelLocation.Local(source), PanelLocation.Local(target), false),
                size, lastWriteTimeUtc) { }

        public string SourcePath => Mapping.Source.SourcePath;
        public string DestinationPath => Mapping.Destination.SourcePath;
    }

    private sealed record CopyAttemptResult(FileInfo SourceInfo, FileAttributes SourceAttributes);

    private sealed record CopyDirectoryPlanItem(FileOperationPlanItem Mapping)
    {
        public CopyDirectoryPlanItem(string source, string target)
            : this(new FileOperationPlanItem(PanelLocation.Local(source), PanelLocation.Local(target), true)) { }

        public string SourcePath => Mapping.Source.SourcePath;
        public string DestinationPath => Mapping.Destination.SourcePath;
    }

    private readonly record struct CopyOutcome(bool CopiedSomething, bool Completed, IReadOnlyList<string> SuccessfullyCopiedSources);

    private enum CopyDestinationAction
    {
        CreateOrOverwrite,
        ResumeWithTailValidation,
    }

    private readonly record struct CopyDestination(
        string Path,
        CopyDestinationAction Action,
        long ResumeOffset = 0,
        long ResumeRollbackBytes = 0,
        SafeLocalFilePublisher.DestinationSnapshot? ExpectedDestination = null)
    {
        public static CopyDestination Skip { get; } = new(string.Empty, CopyDestinationAction.CreateOrOverwrite);
    }

    private sealed class CopyRetryableIOException : IOException
    {
        public CopyRetryableIOException(string failureKind, Exception innerException)
            : base(innerException.Message, innerException)
        {
            FailureKind = failureKind;
        }

        public string FailureKind { get; }
    }

    private sealed class OperationState : IFileOperationErrorSink
    {
        private readonly FileOperationKind _kind;
        private readonly IProgress<FileOperationProgress>? _progress;
        private readonly IFileOperationPauseController? _pauseController;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Stopwatch _progressStopwatch = new();
        private long _totalBytes;
        private int _totalItems;
        private long _bytesProcessed;
        private int _itemsDone;
        private int _foldersDone;

        public OperationState(
            FileOperationKind kind,
            IProgress<FileOperationProgress>? progress,
            IFileOperationPauseController? pauseController)
        {
            _kind = kind;
            _progress = progress;
            _pauseController = pauseController;
        }

        public void WaitIfPaused(CancellationToken cancellationToken) =>
            _pauseController?.WaitIfPaused(cancellationToken);

        public int CopiedCount { get; set; }
        public int MovedCount { get; set; }
        public int DeletedCount { get; set; }
        public int CreatedDirectoryCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Cancelled { get; set; }
        public bool PartiallyMoved { get; set; }
        public bool PublicationUnknown { get; set; }
        public FileConflictDecisionPolicy Conflicts { get; } = new();
        public List<FileOperationItemError> Errors { get; } = [];
        public List<FileOperationItemError> Warnings { get; } = [];
        public void AddWarning(string path, string message) => Warnings.Add(new FileOperationItemError { Path = path, Message = message });

        public void SetTotals(long totalBytes, int totalItems)
        {
            _totalBytes = totalBytes;
            _totalItems = totalItems;
        }

        public void StartProgressTimer()
        {
            if (!_progressStopwatch.IsRunning)
                _progressStopwatch.Start();
        }

        public void AddBytes(long bytes) => _bytesProcessed += bytes;

        public void CompleteItem() => _itemsDone++;

        public void CompleteFolder() => _foldersDone++;

        public void AddError(string path, string message)
        {
            Errors.Add(new FileOperationItemError { Path = path, Message = message });
        }

        public void ReportScanning(string currentPath, int fileCount, int folderCount, long byteCount)
        {
            _progress?.Report(new FileOperationProgress
            {
                Kind = _kind,
                Phase = FileOperationPhase.Scanning,
                CurrentPath = currentPath,
                CurrentBytesDone = 0,
                CurrentBytesTotal = 0,
                TotalBytesDone = byteCount,
                TotalBytesTotal = 0,
                ItemsDone = fileCount,
                ItemsTotal = 0,
                FoldersDone = folderCount,
                BytesPerSecond = 0,
                TimeRemaining = null,
                Elapsed = _stopwatch.Elapsed,
            });
        }

        public void ReportValidation(
            string currentPath,
            string currentDestinationPath,
            long currentBytesTotal,
            string statusMessage,
            long? resumeOffset,
            long? rollbackBytes)
        {
            long acceptedBytes = resumeOffset.GetValueOrDefault();
            _progress?.Report(new FileOperationProgress
            {
                Kind = _kind,
                Phase = FileOperationPhase.Validating,
                CurrentPath = currentPath,
                CurrentDestinationPath = currentDestinationPath,
                StatusMessage = statusMessage,
                CurrentBytesDone = acceptedBytes,
                CurrentBytesTotal = currentBytesTotal,
                TotalBytesDone = _bytesProcessed + acceptedBytes,
                TotalBytesTotal = _totalBytes,
                ResumeOffset = resumeOffset,
                ResumeRollbackBytes = rollbackBytes,
                ItemsDone = _itemsDone,
                ItemsTotal = _totalItems,
                FoldersDone = _foldersDone,
                BytesPerSecond = 0,
                TimeRemaining = null,
                Elapsed = _progressStopwatch.Elapsed,
            });
        }

        public void ReportDeleting(string currentPath, long currentBytesDone, long currentBytesTotal)
        {
            double seconds = Math.Max(_progressStopwatch.Elapsed.TotalSeconds, 0.001);
            double speed = _bytesProcessed / seconds;
            TimeSpan? remaining = speed <= 0 || _totalBytes <= _bytesProcessed
                ? null
                : TimeSpan.FromSeconds((_totalBytes - _bytesProcessed) / speed);

            _progress?.Report(new FileOperationProgress
            {
                Kind = _kind,
                Phase = FileOperationPhase.Deleting,
                CurrentPath = currentPath,
                StatusMessage = "Deleting the file",
                CurrentBytesDone = currentBytesDone,
                CurrentBytesTotal = currentBytesTotal,
                TotalBytesDone = _bytesProcessed,
                TotalBytesTotal = _totalBytes,
                ItemsDone = _itemsDone,
                ItemsTotal = _totalItems,
                FoldersDone = _foldersDone,
                BytesPerSecond = speed,
                TimeRemaining = remaining,
                Elapsed = _progressStopwatch.Elapsed,
            });
        }

        public void Report(string currentPath, string? currentDestinationPath, long currentBytesDone, long currentBytesTotal)
        {
            double seconds = Math.Max(_progressStopwatch.Elapsed.TotalSeconds, 0.001);
            double speed = _bytesProcessed / seconds;
            TimeSpan? remaining = speed <= 0 || _totalBytes <= _bytesProcessed
                ? null
                : TimeSpan.FromSeconds((_totalBytes - _bytesProcessed) / speed);

            _progress?.Report(new FileOperationProgress
            {
                Kind = _kind,
                Phase = FileOperationPhase.Copying,
                CurrentPath = currentPath,
                CurrentDestinationPath = currentDestinationPath,
                CurrentBytesDone = currentBytesDone,
                CurrentBytesTotal = currentBytesTotal,
                TotalBytesDone = _bytesProcessed,
                TotalBytesTotal = _totalBytes,
                ItemsDone = _itemsDone,
                ItemsTotal = _totalItems,
                FoldersDone = _foldersDone,
                BytesPerSecond = speed,
                TimeRemaining = remaining,
                Elapsed = _progressStopwatch.Elapsed,
            });
        }

        public void ReportCommitting(string destinationPath)
        {
            _progress?.Report(new FileOperationProgress
            {
                Kind = _kind,
                Phase = FileOperationPhase.Committing,
                CurrentPath = destinationPath,
                StatusMessage = "Committing changes",
                TotalBytesDone = _bytesProcessed,
                TotalBytesTotal = _totalBytes,
                ItemsDone = _itemsDone,
                ItemsTotal = _totalItems,
                FoldersDone = _foldersDone,
                Elapsed = _progressStopwatch.Elapsed,
            });
        }

        public FileOperationResult ToResult() =>
            new()
            {
                Kind = _kind,
                CopiedCount = CopiedCount,
                MovedCount = MovedCount,
                DeletedCount = DeletedCount,
                CreatedDirectoryCount = CreatedDirectoryCount,
                SkippedCount = SkippedCount,
                Cancelled = Cancelled,
                PartiallyMoved = PartiallyMoved,
                PublicationUnknown = PublicationUnknown,
                BytesProcessed = _bytesProcessed,
                TotalBytes = _totalBytes,
                Errors = Errors,
                Warnings = Warnings,
            };
    }
}
