using System.Diagnostics;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

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
}

public sealed class FileOperationService : IFileOperationService
{
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly TimeSpan ReliableCopyRetryDelay = TimeSpan.FromSeconds(1);
    private const int ReliableCopyRetryCount = 20;
    private readonly IFilePanelSourceRegistry? _sources;
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

        _sources = sources;
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
                    await CopyAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                    break;
                case FileOperationKind.Move:
                    await MoveAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                    break;
                case FileOperationKind.Delete:
                    Delete(request, state, cancellationToken);
                    break;
                case FileOperationKind.CreateDirectory:
                    CreateDirectory(request, state, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported file operation.");
            }
        }
        catch (OperationCanceledException)
        {
            state.Cancelled = true;
        }

        return state.ToResult();
    }

    private static bool UsesProviderLocations(FileOperationRequest request)
    {
        if (request.SourceLocations is null && request.DestinationLocation is null)
            return false;

        bool hasRemoteSource = request.SourceLocations?.Any(l => l.SourceId != PanelSourceId.Local) == true;
        bool hasRemoteDestination = request.DestinationLocation is { SourceId: var sourceId } &&
                                    sourceId != PanelSourceId.Local;
        return hasRemoteSource || hasRemoteDestination;
    }

    private void ValidateAccessibleSources(FileOperationRequest request)
    {
        if (_sources is null)
            return;

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
        if (_sources is null)
            throw new InvalidOperationException("Provider-aware file operations require a panel source registry.");

        switch (request.Kind)
        {
            case FileOperationKind.Copy:
                await CopyProviderAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.Move:
                await MoveProviderAsync(request, conflictResolver, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.Delete:
                await DeleteProviderAsync(request, state, cancellationToken).ConfigureAwait(false);
                break;
            case FileOperationKind.CreateDirectory:
                await CreateProviderDirectoryAsync(request, state, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported file operation.");
        }
    }

    private async Task CopyProviderAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        ValidateProviderCopyOptions(request.Options);

        var destination = RequireDestinationLocation(request);
        var sources = RequireSourceLocations(request);
        if (destination.SourceId != PanelSourceId.Local &&
            sources.Any(source => source.SourceId != PanelSourceId.Local && source.SourceId != destination.SourceId))
        {
            throw new InvalidOperationException("Provider-to-provider copy is not supported.");
        }

        var destinationSource = _sources!.GetSource(destination.SourceId);
        var plan = BuildProviderCopyPlan(sources, destination, request.Options, cancellationToken);

        state.SetTotals(CalculateProviderPlanSize(plan), plan.Count);
        state.StartProgressTimer();

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
                    plannedItem.Source,
                    plannedItem.SourcePath,
                    destinationSource,
                    plannedItem.TargetPath,
                    plannedItem.Item!,
                    request.Options,
                    conflictResolver,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task CopyProviderItemAsync(
        IFilePanelSource source,
        string sourcePath,
        IFilePanelSource destinationSource,
        string destinationPath,
        FilePanelItem sourceItem,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        if (sourceItem.IsDirectory)
        {
            if (source.SourceId == destinationSource.SourceId)
                ValidateProviderCopyTarget(source, sourcePath, destinationPath, sourceItem.IsDirectory);
            await destinationSource.CreateDirectoryAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            state.CompleteFolder();

            foreach (var child in source.EnumerateDirectory(sourcePath, cancellationToken).Where(i => !i.IsParentDirectory))
            {
                string childDestination = CombineProviderPath(
                    destinationSource.SourceId,
                    destinationPath,
                    child.Name);
                await CopyProviderItemAsync(
                        source,
                        child.SourcePath,
                        destinationSource,
                        childDestination,
                        child,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            state.CompleteItem();
            return;
        }

        state.WaitIfPaused(cancellationToken);
        var destinationItem = destinationSource.GetItem(destinationPath, cancellationToken);
        if (destinationItem is not null)
        {
            var decision = options.DefaultConflictDecision == ConflictDecisionMode.Ask
                ? conflictResolver.Resolve(new FileOperationConflict
                {
                    SourcePath = sourcePath,
                    DestinationPath = destinationPath,
                    SourceIsDirectory = false,
                    DestinationIsDirectory = destinationItem.IsDirectory,
                    SourceSize = sourceItem.Size,
                    DestinationSize = destinationItem.Size,
                    SourceLastWriteTime = sourceItem.LastWriteTime,
                    DestinationLastWriteTime = destinationItem.LastWriteTime,
                })
                : FileOperationConflictDecision.FromMode(options.DefaultConflictDecision);

            switch (decision.Mode)
            {
                case ConflictDecisionMode.Skip:
                case ConflictDecisionMode.SkipAll:
                    state.SkippedCount++;
                    state.CompleteItem();
                    return;
                case ConflictDecisionMode.Rename:
                case ConflictDecisionMode.RenameAll:
                    destinationPath = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                        ? GenerateProviderName(destinationSource, destinationPath, cancellationToken)
                        : decision.NewDestinationPath;
                    if (source.SourceId == destinationSource.SourceId)
                        ValidateProviderCopyTarget(source, sourcePath, destinationPath, sourceItem.IsDirectory);
                    break;
                case ConflictDecisionMode.Cancel:
                    throw new OperationCanceledException("File operation cancelled by user.");
                case ConflictDecisionMode.Overwrite:
                case ConflictDecisionMode.OverwriteAll:
                    await destinationSource.DeleteAsync(destinationPath, recursive: false, cancellationToken).ConfigureAwait(false);
                    break;
                case ConflictDecisionMode.OnlyNewer:
                    throw new InvalidOperationException("Only newer is not supported for provider copy.");
            }
        }

        await EnsureProviderDestinationDirectoryAsync(destinationSource, destinationPath, cancellationToken).ConfigureAwait(false);
        await using var input = await source.OpenReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await using var output = await destinationSource.OpenWriteAsync(destinationPath, overwrite: true, cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[CopyBufferSize];
        long done = 0;
        long total = sourceItem.Size ?? 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            state.AddBytes(read);
            state.Report(sourcePath, destinationPath, done, total);
        }

        state.CopiedCount++;
        state.CompleteItem();
    }

    private async Task MoveProviderAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        ValidateMoveConflictDecision(request.Options.DefaultConflictDecision);

        var sources = RequireSourceLocations(request);
        var sourceLocation = sources[0];
        var destination = RequireDestinationLocation(request);
        if (sources.Any(source => source.SourceId != destination.SourceId))
            throw new InvalidOperationException("Cross-provider move is not supported.");

        var source = _sources!.GetSource(sourceLocation.SourceId);
        var plan = BuildProviderMovePlan(source, sources, destination.SourcePath, request.Options, cancellationToken);
        state.SetTotals(CalculateProviderPlanSize(plan), plan.Count);
        state.StartProgressTimer();

        foreach (var plannedItem in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plannedItem.Item is null)
            {
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            string targetPath = plannedItem.TargetPath;
            if (ProviderPathRelations.PathsEqual(source, plannedItem.SourcePath, targetPath))
            {
                state.CompleteItem();
                continue;
            }

            FilePanelItem? destinationItem = source.GetItem(targetPath, cancellationToken);
            if (destinationItem is not null)
            {
                EnsureMoveTypeCompatibility(plannedItem.Item!, destinationItem);
                var decision = request.Options.DefaultConflictDecision == ConflictDecisionMode.Ask
                    ? conflictResolver.Resolve(BuildProviderConflict(plannedItem.Item!, destinationItem, targetPath))
                    : FileOperationConflictDecision.FromMode(request.Options.DefaultConflictDecision);

                switch (decision.Mode)
                {
                    case ConflictDecisionMode.Skip:
                    case ConflictDecisionMode.SkipAll:
                        state.SkippedCount++;
                        state.CompleteItem();
                        continue;
                    case ConflictDecisionMode.Rename:
                    case ConflictDecisionMode.RenameAll:
                        targetPath = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                            ? GenerateProviderName(source, targetPath, cancellationToken)
                            : decision.NewDestinationPath;
                        ValidateProviderMoveTarget(source, plannedItem.SourcePath, targetPath, plannedItem.Item!.IsDirectory);
                        break;
                    case ConflictDecisionMode.Cancel:
                        throw new OperationCanceledException("File operation cancelled by user.");
                    case ConflictDecisionMode.Overwrite:
                    case ConflictDecisionMode.OverwriteAll:
                        await source.DeleteAsync(targetPath, destinationItem.IsDirectory, cancellationToken).ConfigureAwait(false);
                        break;
                    case ConflictDecisionMode.OnlyNewer:
                        throw new InvalidOperationException("Only newer is only supported for copy operations.");
                }
            }

            await source.RenameAsync(plannedItem.SourcePath, targetPath, cancellationToken).ConfigureAwait(false);
            state.MovedCount++;
            state.AddBytes(plannedItem.Item!.Size ?? 0);
            state.CompleteItem();
        }
    }

    private async Task DeleteProviderAsync(
        FileOperationRequest request,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var sources = RequireSourceLocations(request);
        state.SetTotals(CalculateProviderSourcesSize(sources, cancellationToken), sources.Count);
        state.StartProgressTimer();

        foreach (var sourceLocation in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _sources!.GetSource(sourceLocation.SourceId);
            var item = source.GetItem(sourceLocation.SourcePath, cancellationToken);
            if (item is null)
            {
                state.ReportDeleting(sourceLocation.SourcePath, 0, 0);
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            long bytes = CalculateProviderSourceSize(sourceLocation, cancellationToken);
            state.ReportDeleting(sourceLocation.SourcePath, 0, bytes);
            await source.DeleteAsync(sourceLocation.SourcePath, item.IsDirectory, cancellationToken).ConfigureAwait(false);
            state.DeletedCount++;
            state.AddBytes(bytes);
            state.CompleteItem();
            state.ReportDeleting(sourceLocation.SourcePath, bytes, bytes);
        }
    }

    private async Task CreateProviderDirectoryAsync(
        FileOperationRequest request,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var destination = RequireDestinationLocation(request);
        var source = _sources!.GetSource(destination.SourceId);
        await source.CreateDirectoryAsync(destination.SourcePath, cancellationToken).ConfigureAwait(false);
        state.CreatedDirectoryCount++;
        state.SetTotals(0, 1);
        state.CompleteItem();
    }

    private async Task<CopyOutcome> CopyAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken,
        string? rootTarget = null)
    {
        string destination = RequireDestination(request);
        var plan = BuildCopyPlan(request.Sources, destination, request.Options, state, cancellationToken, rootTarget);
        bool copiedAny = false;
        bool completed = true;
        var successfullyCopiedSources = new List<string>();
        state.SetTotals(plan.TotalBytes, plan.FileCount + plan.DirectoryCount);
        state.StartProgressTimer();

        foreach (var directory in plan.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateDirectoryTarget(directory.DestinationPath);
            copiedAny = true;
            state.CompleteItem();
            state.CompleteFolder();
        }

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.WaitIfPaused(cancellationToken);
            CopyDestination copyTarget = CopyDestination.Skip;
            try
            {
                copyTarget = await ResolveDestinationPathAsync(file, request.Options, conflictResolver, state, cancellationToken)
                    .ConfigureAwait(false);
                if (copyTarget.Path.Length == 0)
                {
                    completed = false;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(copyTarget.Path)!);

                if (copyTarget.Action == CopyDestinationAction.CreateOrOverwrite &&
                    IsReparsePoint(file.SourcePath) &&
                    request.Options.SymlinkMode == SymlinkCopyMode.CopyLink)
                {
                    if (!CopyReparsePoint(file.SourcePath, copyTarget.Path, request.Options, state))
                    {
                        completed = false;
                        continue;
                    }
                    state.CopiedCount++;
                    state.AddBytes(file.Size);
                    state.CompleteItem();
                    state.Report(file.SourcePath, copyTarget.Path, file.Size, file.Size);
                    copiedAny = true;
                    successfullyCopiedSources.Add(file.SourcePath);
                    continue;
                }

                bool copied = await CopyFileContentsAsync(
                        file,
                        copyTarget,
                        request.Options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!copied)
                {
                    completed = false;
                    continue;
                }
                copiedAny = true;
                successfullyCopiedSources.Add(file.SourcePath);
            }
            catch (CopyRetryableIOException ex) when (
                request.Options.CopyMode == CopyMode.FastSalvage &&
                ex.FailureKind == "Read failed")
            {
                completed = false;
                string partialPath = copyTarget.Path.Length == 0 ? file.DestinationPath : copyTarget.Path;
                CleanupPartialDestination(partialPath, request.Options, state);
                state.AddError(file.SourcePath, ex.Message);
                state.SkippedCount++;
                state.CompleteItem();
                state.ReportValidation(
                    file.SourcePath,
                    partialPath,
                    file.Size,
                    "Read failed, skipping file...",
                    resumeOffset: null,
                    rollbackBytes: null);
            }
        }

        PreserveDirectoryTargetsMetadata(plan.Directories, request.Options, state, cancellationToken);
        return new CopyOutcome(copiedAny, completed, successfullyCopiedSources);
    }

    private async Task MoveAsync(
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
        var plan = BuildLocalMovePlan(request.Sources, effectiveDestination, singleRename, request.Options, cancellationToken);
        state.SetTotals(CalculateSourcesSize(request.Sources), request.Sources.Count);
        state.StartProgressTimer();

        foreach (var item in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string finalTarget = item.TargetPath;
            if (!HasFileMask(request.Options) && TryMoveDirect(item.SourcePath, item.TargetPath, request.Options, conflictResolver, state, out finalTarget))
                continue;

            CopyOutcome outcome = await CopyAsync(
                request with { Kind = FileOperationKind.Copy, Sources = [item.SourcePath], Destination = Path.GetDirectoryName(finalTarget)! },
                conflictResolver,
                state,
                cancellationToken,
                finalTarget).ConfigureAwait(false);

            if (HasFileMask(request.Options))
            {
                foreach (string sourcePath in outcome.SuccessfullyCopiedSources)
                {
                    File.Delete(sourcePath);
                    state.MovedCount++;
                }
                continue;
            }

            if (!outcome.CopiedSomething || !outcome.Completed)
                continue;

            DeletePath(item.SourcePath, useRecycleBin: false);
            state.MovedCount++;
        }
    }

    private static bool HasFileMask(FileOperationOptions options) => !string.IsNullOrWhiteSpace(options.FileMask);

    private static List<LocalMovePlanItem> BuildLocalMovePlan(
        IReadOnlyList<string> sources,
        string destination,
        bool singleRename,
        FileOperationOptions options,
        CancellationToken cancellationToken)
    {
        DestinationPattern pattern = DestinationPattern.Parse(destination, PanelSourceId.Local);
        var plan = new List<LocalMovePlanItem>(sources.Count);
        bool hasMask = HasFileMask(options);
        var matcher = new FarMaskMatcher();
        var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasMask && !IsSelectedMoveSource(source, options.FileMask!, matcher, groups, cancellationToken))
                continue;

            string target = pattern.HasWildcards
                ? Path.Combine(string.IsNullOrEmpty(pattern.ParentPath) ? Path.GetDirectoryName(source)! : pattern.ParentPath, pattern.TransformName(Path.GetFileName(source)))
                : singleRename
                    ? destination
                    : Path.Combine(destination, Path.GetFileName(source));
            plan.Add(new LocalMovePlanItem(source, target));
        }

        ValidateLocalMovePlan(plan, pattern.HasWildcards);
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

    private void Delete(FileOperationRequest request, OperationState state, CancellationToken cancellationToken)
    {
        state.SetTotals(CalculateSourcesSize(request.Sources), request.Sources.Count);
        state.StartProgressTimer();

        foreach (string path in request.Sources)
        {
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

                long bytes = CalculateSourceSize(path);
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

    private CopyPlan BuildCopyPlan(
        IReadOnlyList<string> sources,
        string destination,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken,
        string? rootTarget = null)
    {
        DestinationPattern destinationPattern = DestinationPattern.Parse(destination, PanelSourceId.Local);
        string destinationDirectory = destinationPattern.HasWildcards ? destinationPattern.ParentPath : destination;
        var files = new List<CopyFilePlanItem>();
        var directories = new List<CopyDirectoryPlanItem>();
        var matcher = new FarMaskMatcher();
        var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
        bool hasMask = !string.IsNullOrWhiteSpace(options.FileMask);
        long plannedBytes = 0;

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(source))
            {
                string fileName = Path.GetFileName(source);
                if (hasMask && !matcher.IsMatch(options.FileMask!, fileName, groups))
                    continue;

                string target = rootTarget ?? Path.Combine(destinationDirectory, destinationPattern.HasWildcards
                    ? destinationPattern.TransformName(fileName)
                    : fileName);
                if (PathsEqual(source, target))
                    throw new IOException("Source and destination file are the same.");
                var snapshot = GetSourceSnapshot(source);
                files.Add(new CopyFilePlanItem(source, target, snapshot.Length, snapshot.LastWriteTimeUtc));
                plannedBytes += snapshot.Length;
                state.ReportScanning(Path.GetDirectoryName(source) ?? source, files.Count, directories.Count, plannedBytes);
                continue;
            }

            if (!Directory.Exists(source))
                continue;

            if (hasMask && !DirectoryContainsMatchingFile(source, options.FileMask!, matcher, groups, cancellationToken))
                continue;

            string rootDestination = rootTarget ?? Path.Combine(destinationDirectory, destinationPattern.HasWildcards
                ? destinationPattern.TransformName(Path.GetFileName(source))
                : Path.GetFileName(source));
            if (PathsEqual(source, rootDestination))
                throw new IOException("Source and destination directory are the same.");
            if (IsPathInside(rootDestination, source))
                throw new IOException("Cannot copy a directory into itself.");

            if (!hasMask)
            {
                directories.Add(new CopyDirectoryPlanItem(source, rootDestination));
                state.ReportScanning(source, files.Count, directories.Count, plannedBytes);
            }

            AddDirectoryPlan(source, rootDestination, hasMask, options.FileMask, matcher, groups, directories, files, ref plannedBytes, state, cancellationToken);
        }

        if (destinationPattern.HasWildcards)
            ValidateLocalPlanCollisions(files.Select(file => file.DestinationPath).Concat(directories.Select(directory => directory.DestinationPath)));

        return new CopyPlan(
            files,
            directories,
            files.Sum(f => f.Size),
            files.Count,
            directories.Count);
    }

    private static void AddDirectoryPlan(
        string sourceDirectory,
        string destinationDirectory,
        bool hasMask,
        string? fileMask,
        FarMaskMatcher matcher,
        IReadOnlyDictionary<string, MaskGroup> groups,
        List<CopyDirectoryPlanItem> directories,
        List<CopyFilePlanItem> files,
        ref long plannedBytes,
        OperationState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.ReportScanning(sourceDirectory, files.Count, directories.Count, plannedBytes);

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            if (!hasMask)
            {
                directories.Add(new CopyDirectoryPlanItem(directory, target));
                state.ReportScanning(directory, files.Count, directories.Count, plannedBytes);
            }
            AddDirectoryPlan(directory, target, hasMask, fileMask, matcher, groups, directories, files, ref plannedBytes, state, cancellationToken);
        }

        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(file);
            if (hasMask && !matcher.IsMatch(fileMask!, fileName, groups))
                continue;

            var snapshot = GetSourceSnapshot(file);
            files.Add(new CopyFilePlanItem(file, Path.Combine(destinationDirectory, fileName), snapshot.Length, snapshot.LastWriteTimeUtc));
            plannedBytes += snapshot.Length;
            state.ReportScanning(sourceDirectory, files.Count, directories.Count, plannedBytes);
        }
    }

    private async Task<CopyDestination> ResolveDestinationPathAsync(
        CopyFilePlanItem file,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string destinationPath = file.DestinationPath;
        while (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            ConflictDecisionMode mode = ResolveConfiguredConflictDecision(options, state);
            string? newDestination = null;

            if (options.CopyMode == CopyMode.Reliable)
            {
                while (true)
                {
                    CopyDestination? resumeDestination = TryResolveTailValidatedResume(
                        file,
                        destinationPath,
                        options,
                        state,
                        cancellationToken,
                        out CopyResumePlan? sourceReadFailure);
                    if (resumeDestination is not null)
                        return resumeDestination.Value;

                    if (sourceReadFailure is null)
                        break;

                    await WaitForReliableRetryAsync(
                            file,
                            destinationPath,
                            state,
                            "Read failed",
                            sourceReadFailure.Reason,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (mode == ConflictDecisionMode.Ask)
            {
                var decision = conflictResolver.Resolve(BuildConflict(file.SourcePath, destinationPath));
                mode = decision.Mode;
                newDestination = decision.NewDestinationPath;
            }

            mode = NormalizeRememberedConflictDecision(mode, state);

            switch (mode)
            {
                case ConflictDecisionMode.Overwrite:
                    if (Directory.Exists(destinationPath))
                        throw new IOException("Cannot overwrite a directory with a file.");
                    DeleteFileTarget(destinationPath);
                    return new CopyDestination(destinationPath, CopyDestinationAction.CreateOrOverwrite);
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
                        DeleteFileTarget(destinationPath);
                        return new CopyDestination(destinationPath, CopyDestinationAction.CreateOrOverwrite);
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

    private static ConflictDecisionMode ResolveConfiguredConflictDecision(
        FileOperationOptions options,
        OperationState state)
    {
        return state.StickyConflictDecision switch
        {
            ConflictDecisionMode.Skip => ConflictDecisionMode.Skip,
            ConflictDecisionMode.Overwrite => ConflictDecisionMode.Overwrite,
            ConflictDecisionMode.Rename => ConflictDecisionMode.Rename,
            _ => options.DefaultConflictDecision,
        };
    }

    private static ConflictDecisionMode NormalizeRememberedConflictDecision(
        ConflictDecisionMode mode,
        OperationState state)
    {
        switch (mode)
        {
            case ConflictDecisionMode.OverwriteAll:
                state.StickyConflictDecision = ConflictDecisionMode.Overwrite;
                return ConflictDecisionMode.Overwrite;
            case ConflictDecisionMode.SkipAll:
                state.StickyConflictDecision = ConflictDecisionMode.Skip;
                return ConflictDecisionMode.Skip;
            case ConflictDecisionMode.RenameAll:
                state.StickyConflictDecision = ConflictDecisionMode.Rename;
                return ConflictDecisionMode.Rename;
            default:
                return mode;
        }
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
        bool reliableRetry = options.CopyMode == CopyMode.Reliable;
        bool wrapFailures = reliableRetry || options.CopyMode == CopyMode.FastSalvage;
        CopyDestination copyTarget = initialTarget;
        long currentBytes = CurrentBytesForTarget(copyTarget);
        long accountedCurrentBytes = 0;
        int retryNumber = 0;
        AccountCurrentBytes(currentBytes);
        state.Report(file.SourcePath, copyTarget.Path, currentBytes, file.Size);

        CopyAttemptResult copyResult;
        while (true)
        {
            try
            {
                copyResult = await CopyFileAttemptAsync(
                        file,
                        copyTarget,
                        currentBytes,
                        wrapFailures,
                        AccountCurrentBytes,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            }
            catch (CopyRetryableIOException ex) when (reliableRetry && retryNumber < ReliableCopyRetryCount)
            {
                retryNumber++;
                await WaitForReliableRetryAsync(
                        file,
                        copyTarget.Path,
                        state,
                        ex.FailureKind,
                        ex.InnerException?.Message ?? ex.Message,
                        cancellationToken)
                    .ConfigureAwait(false);

                copyTarget = await ResolveDestinationPathAsync(
                        file,
                        options,
                        conflictResolver,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (copyTarget.Path.Length == 0)
                    return false;

                currentBytes = CurrentBytesForTarget(copyTarget);
                AccountCurrentBytes(currentBytes);
                state.Report(file.SourcePath, copyTarget.Path, currentBytes, file.Size);
            }
        }

        _platformOperations.PreserveFileMetadata(file.SourcePath, copyTarget.Path, options, state);

        state.CopiedCount++;
        state.CompleteItem();
        return true;

        void AccountCurrentBytes(long newCurrentBytes)
        {
            long delta = newCurrentBytes - accountedCurrentBytes;
            if (delta != 0)
                state.AddBytes(delta);
            accountedCurrentBytes = newCurrentBytes;
        }
    }

    private async Task<CopyAttemptResult> CopyFileAttemptAsync(
        CopyFilePlanItem file,
        CopyDestination copyTarget,
        long startOffset,
        bool wrapSourceReadFailures,
        Action<long> accountCurrentBytes,
        OperationState state,
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
            state.Report(file.SourcePath, copyTarget.Path, currentBytes, file.Size);
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

    private static void CreateDirectoryTarget(string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
    }

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

    private bool TryMoveDirect(
        string source,
        string destination,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        out string finalTarget)
    {
        finalTarget = destination;
        if (PathsEqual(source, destination))
        {
            state.SkippedCount++;
            state.CompleteItem();
            return true;
        }

        bool sourceIsFile = File.Exists(source);
        bool sourceIsDirectory = Directory.Exists(source);
        if (!sourceIsFile && !sourceIsDirectory)
        {
            state.SkippedCount++;
            state.CompleteItem();
            return true;
        }

        if (sourceIsDirectory && IsPathInside(destination, source))
            throw new IOException("Cannot move a directory into itself.");

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            if ((sourceIsFile && Directory.Exists(destination)) || (sourceIsDirectory && File.Exists(destination)))
                throw new IOException("Cannot overwrite a file with a directory or a directory with a file.");

            var decision = options.DefaultConflictDecision == ConflictDecisionMode.Ask
                ? conflictResolver.Resolve(BuildConflict(source, destination))
                : FileOperationConflictDecision.FromMode(options.DefaultConflictDecision);

            switch (decision.Mode)
            {
                case ConflictDecisionMode.Skip:
                case ConflictDecisionMode.SkipAll:
                    state.SkippedCount++;
                    state.CompleteItem();
                    return true;
                case ConflictDecisionMode.Cancel:
                    throw new OperationCanceledException("Move cancelled by user.");
                case ConflictDecisionMode.Rename:
                case ConflictDecisionMode.RenameAll:
                    destination = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                        ? GenerateName(destination)
                        : decision.NewDestinationPath;
                    finalTarget = destination;
                    break;
                case ConflictDecisionMode.OnlyNewer:
                    throw new InvalidOperationException("Only newer is only supported for copy operations.");
            }

            if (File.Exists(destination))
                DeleteFileTarget(destination);
            else if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
        }

        try
        {
            if (_dependencies.ForceMoveFallback(source, destination))
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (sourceIsFile)
                File.Move(source, destination);
            else
                Directory.Move(source, destination);

            state.MovedCount++;
            state.AddBytes(sourceIsFile ? GetFileSize(destination) : 0);
            state.CompleteItem();
            state.Report(source, destination, sourceIsFile ? GetFileSize(destination) : 0, sourceIsFile ? GetFileSize(destination) : 0);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private bool CopyReparsePoint(
        string sourcePath,
        string destinationPath,
        FileOperationOptions options,
        OperationState state)
    {
        if (options.SymlinkMode == SymlinkCopyMode.CopyTargetContents)
            return true;

        if (!_platformOperations.TryCopySymbolicLink(sourcePath, destinationPath, out string? error))
        {
            state.AddError(sourcePath, error ?? "Cannot copy symbolic link.");
            return false;
        }

        return true;
    }

    private void DeletePath(string path, bool useRecycleBin)
    {
        if (File.Exists(path))
            _platformOperations.DeleteFile(path, useRecycleBin);
        else if (Directory.Exists(path))
            _platformOperations.DeleteDirectory(path, recursive: true, useRecycleBin);
    }

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

    private static long CalculateSourcesSize(IReadOnlyList<string> sources)
    {
        long total = 0;
        foreach (string source in sources)
            total += CalculateSourceSize(source);

        return total;
    }

    private static long CalculateSourceSize(string source)
    {
        if (File.Exists(source))
            return GetFileSize(source);

        return Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories).Sum(GetFileSize)
            : 0;
    }

    private static long GetFileSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static CopyResumeSourceSnapshot GetSourceSnapshot(string path)
    {
        var source = new FileInfo(path);
        return new CopyResumeSourceSnapshot(source.Length, source.LastWriteTimeUtc);
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

    private static void ValidateMoveConflictDecision(ConflictDecisionMode mode)
    {
        if (mode == ConflictDecisionMode.OnlyNewer)
            throw new InvalidOperationException("Only newer is only supported for copy operations.");
    }

    private static void CleanupPartialDestination(
        string destinationPath,
        FileOperationOptions options,
        OperationState state)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || !File.Exists(destinationPath))
            return;

        if (!options.KeepPartialFilesOnError)
        {
            DeleteFileTarget(destinationPath);
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
        File.Move(destinationPath, partialPath);
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

    private static bool IsPath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar) ||
        Path.IsPathRooted(value);

    private static string RequireDestination(FileOperationRequest request) =>
        string.IsNullOrWhiteSpace(request.Destination)
            ? throw new ArgumentException("Destination is required.", nameof(request))
            : request.Destination;

    private static IReadOnlyList<PanelLocation> RequireSourceLocations(FileOperationRequest request)
    {
        if (request.SourceLocations is { Count: > 0 } sourceLocations)
            return sourceLocations;

        if (request.Sources.Count > 0)
            return request.Sources.Select(PanelLocation.Local).ToList();

        throw new ArgumentException("At least one source is required.", nameof(request));
    }

    private static PanelLocation RequireDestinationLocation(FileOperationRequest request)
    {
        if (request.DestinationLocation is { } destinationLocation)
            return destinationLocation;

        return PanelLocation.Local(RequireDestination(request));
    }

    private List<ProviderOperationPlanItem> BuildProviderCopyPlan(
        IReadOnlyList<PanelLocation> sources,
        PanelLocation destination,
        FileOperationOptions options,
        CancellationToken cancellationToken)
    {
        DestinationPattern destinationPattern = DestinationPattern.Parse(destination.SourcePath, destination.SourceId);
        var plan = new List<ProviderOperationPlanItem>(sources.Count);
        bool hasMask = HasFileMask(options);
        var matcher = new FarMaskMatcher();
        IReadOnlyDictionary<string, MaskGroup> groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (PanelLocation location in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFilePanelSource source = _sources!.GetSource(location.SourceId);
            FilePanelItem? item = source.GetItem(location.SourcePath, cancellationToken);
            string normalizedSourcePath = source.NormalizePath(location.SourcePath);
            if (item is null)
            {
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, null, string.Empty));
                continue;
            }

            string targetPath = CombineProviderPath(
                destination.SourceId,
                destinationPattern.HasWildcards ? destinationPattern.ParentPath : destination.SourcePath,
                destinationPattern.HasWildcards ? destinationPattern.TransformName(item.Name) : item.Name);

            if (!hasMask)
            {
                if (location.SourceId == destination.SourceId)
                    ValidateProviderCopyTarget(source, normalizedSourcePath, targetPath, item.IsDirectory);
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, item, targetPath));
                continue;
            }

            AddMaskedProviderPlanItems(
                plan, location, source, normalizedSourcePath, item, targetPath, options.FileMask!, matcher, groups,
                validateTarget: path =>
                {
                    if (location.SourceId == destination.SourceId)
                        ValidateProviderCopyTarget(source, normalizedSourcePath, path, item.IsDirectory);
                },
                cancellationToken);
        }

        if (destinationPattern.HasWildcards)
            ValidateProviderPlanCollisions(destination.SourceId, plan.Select(item => item.TargetPath));
        return plan;
    }

    private List<ProviderOperationPlanItem> BuildProviderMovePlan(
        IFilePanelSource source,
        IReadOnlyList<PanelLocation> sources,
        string destinationPath,
        FileOperationOptions options,
        CancellationToken cancellationToken)
    {
        DestinationPattern destinationPattern = DestinationPattern.Parse(destinationPath, source.SourceId);
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
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, null, string.Empty));
                continue;
            }

            string targetPath = destinationPattern.HasWildcards
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
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, item, targetPath));
                continue;
            }

            AddMaskedProviderPlanItems(
                plan, location, source, normalizedSourcePath, item, targetPath, options.FileMask!, matcher, groups,
                validateTarget: path => ValidateProviderMoveTarget(source, normalizedSourcePath, path, item.IsDirectory),
                cancellationToken);
        }

        if (destinationPattern.HasWildcards)
            ValidateProviderPlanCollisions(source.SourceId, plan.Select(item => item.TargetPath));
        return plan;
    }

    private static void AddMaskedProviderPlanItems(
        List<ProviderOperationPlanItem> plan,
        PanelLocation rootLocation,
        IFilePanelSource source,
        string rootSourcePath,
        FilePanelItem rootItem,
        string rootTargetPath,
        string fileMask,
        FarMaskMatcher matcher,
        IReadOnlyDictionary<string, MaskGroup> groups,
        Action<string> validateTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!rootItem.IsDirectory)
        {
            if (matcher.IsMatch(fileMask, rootItem.Name, groups))
            {
                validateTarget(rootTargetPath);
                plan.Add(new ProviderOperationPlanItem(rootLocation, source, rootSourcePath, rootItem, rootTargetPath));
            }
            return;
        }

        var files = new List<FilePanelItem>();
        CollectMatchingProviderFiles(source, rootSourcePath, fileMask, matcher, groups, files, cancellationToken);
        if (files.Count == 0)
            return;

        validateTarget(rootTargetPath);
        foreach (FilePanelItem file in files)
        {
            string sourcePath = source.NormalizePath(file.SourcePath);
            string relativePath = GetProviderRelativePath(rootSourcePath, sourcePath);
            string targetPath = CombineProviderRelativePath(source.SourceId, rootTargetPath, relativePath);
            plan.Add(new ProviderOperationPlanItem(file.Location, source, sourcePath, file, targetPath));
        }
    }

    private static void CollectMatchingProviderFiles(
        IFilePanelSource source,
        string directoryPath,
        string fileMask,
        FarMaskMatcher matcher,
        IReadOnlyDictionary<string, MaskGroup> groups,
        List<FilePanelItem> files,
        CancellationToken cancellationToken)
    {
        foreach (FilePanelItem child in source.EnumerateDirectory(directoryPath, cancellationToken).Where(item => !item.IsParentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.IsDirectory)
            {
                CollectMatchingProviderFiles(source, child.SourcePath, fileMask, matcher, groups, files, cancellationToken);
            }
            else if (matcher.IsMatch(fileMask, child.Name, groups))
            {
                files.Add(child);
            }
        }
    }

    private static string GetProviderRelativePath(string rootPath, string childPath)
    {
        string normalizedRoot = rootPath.TrimEnd('/');
        return childPath[(normalizedRoot.Length + 1)..];
    }

    private static string CombineProviderRelativePath(PanelSourceId sourceId, string rootPath, string relativePath) =>
        sourceId == PanelSourceId.Local
            ? Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))
            : rootPath.TrimEnd('/') + "/" + relativePath;

    private static void ValidateProviderCopyTarget(
        IFilePanelSource source,
        string sourcePath,
        string targetPath,
        bool sourceIsDirectory)
    {
        if (ProviderPathRelations.PathsEqual(source, sourcePath, targetPath))
            throw new IOException("Source and destination refer to the same provider item.");

        if (sourceIsDirectory && ProviderPathRelations.IsDescendantOf(source, targetPath, sourcePath))
            throw new IOException("Cannot copy a directory into itself or one of its subdirectories.");
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

    private long CalculateProviderSourcesSize(
        IReadOnlyList<PanelLocation> sources,
        CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var location in sources)
            total += CalculateProviderSourceSize(location, cancellationToken);
        return total;
    }

    private static long CalculateProviderPlanSize(IEnumerable<ProviderOperationPlanItem> plan) =>
        plan.Sum(item => item.Item?.Size ?? 0);

    private static async Task EnsureProviderDestinationDirectoryAsync(
        IFilePanelSource destinationSource,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string? parent = destinationSource.GetParentPath(destinationPath);
        if (string.IsNullOrEmpty(parent) || destinationSource.GetItem(parent, cancellationToken) is not null)
            return;

        await EnsureProviderDestinationDirectoryAsync(destinationSource, parent, cancellationToken).ConfigureAwait(false);
        await destinationSource.CreateDirectoryAsync(parent, cancellationToken).ConfigureAwait(false);
    }

    private long CalculateProviderSourceSize(
        PanelLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _sources!.GetSource(location.SourceId);
        var item = source.GetItem(location.SourcePath, cancellationToken);
        if (item is null)
            return 0;

        if (!item.IsDirectory)
            return item.Size ?? 0;

        long total = 0;
        foreach (var child in source.EnumerateDirectory(location.SourcePath, cancellationToken).Where(i => !i.IsParentDirectory))
            total += CalculateProviderSourceSize(child.Location, cancellationToken);
        return total;
    }

    private static IEnumerable<PanelSourceId> EnumerateReferencedSources(FileOperationRequest request)
    {
        if (request.SourceLocations is { Count: > 0 } sourceLocations)
        {
            foreach (PanelLocation location in sourceLocations)
                yield return location.SourceId;
        }
        else if (request.Sources.Count > 0)
        {
            yield return PanelSourceId.Local;
        }

        if (request.DestinationLocation is { } destinationLocation)
        {
            yield return destinationLocation.SourceId;
        }
        else if (request.Kind is FileOperationKind.Copy or FileOperationKind.Move or FileOperationKind.CreateDirectory &&
                 !string.IsNullOrWhiteSpace(request.Destination))
        {
            yield return PanelSourceId.Local;
        }
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

    private static string CombineProviderPath(PanelSourceId sourceId, string directoryPath, string name)
    {
        if (sourceId == PanelSourceId.Local)
            return Path.Combine(directoryPath, name);

        string trimmedDirectory = directoryPath.TrimEnd('/');
        return trimmedDirectory.Length == 0 || trimmedDirectory == "/"
            ? "/" + name
            : trimmedDirectory + "/" + name;
    }

    private static string GenerateProviderName(
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
            int slash = destinationPath.LastIndexOf('/');
            directory = slash <= 0 ? "/" : destinationPath[..slash];
            string name = slash < 0 ? destinationPath : destinationPath[(slash + 1)..];
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
            string candidate = CombineProviderPath(source.SourceId, directory, $"{fileName} ({i}){extension}");
            if (source.GetItem(candidate, cancellationToken) is null)
                return candidate;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInside(string path, string possibleParent)
    {
        string child = NormalizePath(path);
        string parent = NormalizePath(possibleParent);
        if (!parent.EndsWith(Path.DirectorySeparatorChar))
            parent += Path.DirectorySeparatorChar;

        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void ValidateLocalPlanCollisions(IEnumerable<string> targetPaths)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string targetPath in targetPaths)
        {
            ValidateLocalTargetPath(targetPath);
            if (!targets.Add(NormalizePath(targetPath)))
                throw new IOException($"Multiple source items resolve to the same destination: {targetPath}");
        }
    }

    private static void ValidateLocalMovePlan(IEnumerable<LocalMovePlanItem> plan, bool validateCollisions)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan)
        {
            if (validateCollisions)
            {
                ValidateLocalTargetPath(item.TargetPath);
                if (!targets.Add(NormalizePath(item.TargetPath)))
                    throw new IOException($"Multiple source items resolve to the same destination: {item.TargetPath}");
            }

            if (validateCollisions && PathsEqual(item.SourcePath, item.TargetPath))
                throw new IOException("Source and destination file are the same.");
            if (Directory.Exists(item.SourcePath) && IsPathInside(item.TargetPath, item.SourcePath))
                throw new IOException("Cannot move a directory into itself.");
        }
    }

    private static void ValidateLocalTargetPath(string path)
    {
        string name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['*', '?']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException("Destination pattern produced an invalid file name.");
        }
    }

    private void ValidateProviderPlanCollisions(PanelSourceId sourceId, IEnumerable<string> targetPaths)
    {
        IFilePanelSource destinationSource = _sources!.GetSource(sourceId);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (string targetPath in targetPaths.Where(path => !string.IsNullOrEmpty(path)))
        {
            if (targetPath.IndexOfAny(['*', '?']) >= 0)
                throw new IOException("Destination pattern produced an invalid file name.");
            if (!targets.Add(destinationSource.NormalizePath(targetPath)))
                throw new IOException($"Multiple source items resolve to the same destination: {targetPath}");
        }
    }

    private static string GetProviderParentPath(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    private sealed record CopyFilePlanItem(string SourcePath, string DestinationPath, long Size, DateTime LastWriteTimeUtc);
    private sealed record CopyAttemptResult(FileInfo SourceInfo, FileAttributes SourceAttributes);
    private sealed record CopyDirectoryPlanItem(string SourcePath, string DestinationPath);
    private sealed record LocalMovePlanItem(string SourcePath, string TargetPath);
    private readonly record struct CopyOutcome(bool CopiedSomething, bool Completed, IReadOnlyList<string> SuccessfullyCopiedSources);
    private sealed record ProviderOperationPlanItem(
        PanelLocation Location,
        IFilePanelSource Source,
        string SourcePath,
        FilePanelItem? Item,
        string TargetPath);
    private enum CopyDestinationAction
    {
        CreateOrOverwrite,
        ResumeWithTailValidation,
    }

    private readonly record struct CopyDestination(
        string Path,
        CopyDestinationAction Action,
        long ResumeOffset = 0,
        long ResumeRollbackBytes = 0)
    {
        public static CopyDestination Skip { get; } = new(string.Empty, CopyDestinationAction.CreateOrOverwrite);
    }

    private sealed record CopyPlan(
        IReadOnlyList<CopyFilePlanItem> Files,
        IReadOnlyList<CopyDirectoryPlanItem> Directories,
        long TotalBytes,
        int FileCount,
        int DirectoryCount);

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
        public ConflictDecisionMode? StickyConflictDecision { get; set; }
        public List<FileOperationItemError> Errors { get; } = [];

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
                TotalBytesTotal = byteCount,
                ItemsDone = fileCount,
                ItemsTotal = fileCount,
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
                BytesProcessed = _bytesProcessed,
                TotalBytes = _totalBytes,
                Errors = Errors,
            };
    }
}
