using System.Diagnostics;
using System.Reflection;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace CSharpFar.FileSystem;

public sealed class FileOperationService : IFileOperationService
{
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly CopyResumeAnalyzer ResumeAnalyzer = new();
    private readonly IFilePanelSourceRegistry? _sources;

    public FileOperationService()
    {
    }

    public FileOperationService(IFilePanelSourceRegistry sources)
    {
        _sources = sources;
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
                await MoveProviderAsync(request, state, cancellationToken).ConfigureAwait(false);
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
        var destination = RequireDestinationLocation(request);
        var sources = RequireSourceLocations(request);
        if (destination.SourceId != PanelSourceId.Local &&
            sources.Any(source => source.SourceId != PanelSourceId.Local))
        {
            throw new InvalidOperationException("Provider-to-provider copy is not supported.");
        }

        var destinationSource = _sources!.GetSource(destination.SourceId);

        state.SetTotals(CalculateProviderSourcesSize(sources, cancellationToken), sources.Count);
        state.StartCopying();

        foreach (var sourceLocation in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _sources.GetSource(sourceLocation.SourceId);
            var item = source.GetItem(sourceLocation.SourcePath, cancellationToken);
            if (item is null)
            {
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            string targetPath = CombineProviderPath(
                destination.SourceId,
                destination.SourcePath,
                item.Name);

            await CopyProviderItemAsync(
                    source,
                    sourceLocation.SourcePath,
                    destinationSource,
                    targetPath,
                    item,
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
                    break;
                case ConflictDecisionMode.Cancel:
                    throw new OperationCanceledException("File operation cancelled by user.");
                case ConflictDecisionMode.Overwrite:
                case ConflictDecisionMode.OverwriteAll:
                    await destinationSource.DeleteAsync(destinationPath, recursive: false, cancellationToken).ConfigureAwait(false);
                    break;
                case ConflictDecisionMode.Append:
                case ConflictDecisionMode.AppendAll:
                case ConflictDecisionMode.OnlyNewer:
                case ConflictDecisionMode.ResumeWithTailValidation:
                    throw new InvalidOperationException("The selected conflict mode is not supported for provider copy.");
            }
        }

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
        OperationState state,
        CancellationToken cancellationToken)
    {
        var sources = RequireSourceLocations(request);
        if (sources.Count != 1)
            throw new InvalidOperationException("Provider move supports a single-source rename only.");

        var sourceLocation = sources[0];
        var destination = RequireDestinationLocation(request);
        if (sourceLocation.SourceId != destination.SourceId)
            throw new InvalidOperationException("Cross-provider move is not supported.");

        var source = _sources!.GetSource(sourceLocation.SourceId);
        await source.RenameAsync(sourceLocation.SourcePath, destination.SourcePath, cancellationToken).ConfigureAwait(false);
        state.MovedCount++;
        state.SetTotals(0, 1);
        state.CompleteItem();
    }

    private async Task DeleteProviderAsync(
        FileOperationRequest request,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var sources = RequireSourceLocations(request);
        state.SetTotals(CalculateProviderSourcesSize(sources, cancellationToken), sources.Count);

        foreach (var sourceLocation in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _sources!.GetSource(sourceLocation.SourceId);
            var item = source.GetItem(sourceLocation.SourcePath, cancellationToken);
            if (item is null)
            {
                state.SkippedCount++;
                state.CompleteItem();
                continue;
            }

            await source.DeleteAsync(sourceLocation.SourcePath, item.IsDirectory, cancellationToken).ConfigureAwait(false);
            state.DeletedCount++;
            state.CompleteItem();
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

    private static async Task CopyAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string destination = RequireDestination(request);
        var plan = BuildCopyPlan(request.Sources, destination, request.Options, state, cancellationToken);
        state.SetTotals(plan.TotalBytes, plan.FileCount + plan.DirectoryCount);
        state.StartCopying();

        foreach (var directory in plan.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateDirectoryTarget(directory.DestinationPath, directory.SourcePath, request.Options, state);
            state.CompleteItem();
            state.CompleteFolder();
        }

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.WaitIfPaused(cancellationToken);
            var copyTarget = ResolveDestinationPath(file, request.Options, conflictResolver, state, cancellationToken);
            if (copyTarget.Path.Length == 0)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(copyTarget.Path)!);

            if (copyTarget.Action == CopyDestinationAction.CreateOrOverwrite &&
                IsReparsePoint(file.SourcePath) &&
                request.Options.SymlinkMode == SymlinkCopyMode.CopyLink)
            {
                CopyReparsePoint(file.SourcePath, copyTarget.Path, request.Options, state);
                state.CopiedCount++;
                state.AddBytes(file.Size);
                state.CompleteItem();
                state.Report(file.SourcePath, copyTarget.Path, file.Size, file.Size);
                continue;
            }

            await CopyFileContentsAsync(
                    file.SourcePath,
                    copyTarget.Path,
                    file.Size,
                    copyTarget.Action,
                    copyTarget.ResumeOffset,
                    request.Options,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task MoveAsync(
        FileOperationRequest request,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string destination = RequireDestination(request);
        bool singleRename = request.Sources.Count == 1 && !IsPath(destination);
        string effectiveDestination = singleRename
            ? Path.Combine(Path.GetDirectoryName(request.Sources[0])!, destination)
            : destination;

        if (request.Options.FileMask is null or { Length: 0 })
        {
            state.SetTotals(CalculateSourcesSize(request.Sources), request.Sources.Count);
            state.StartCopying();
            foreach (string source in request.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = singleRename
                    ? effectiveDestination
                    : Path.Combine(effectiveDestination, Path.GetFileName(source));
                if (TryMoveDirect(source, target, request.Options, conflictResolver, state))
                    continue;

                await CopyAsync(
                    request with
                    {
                        Kind = FileOperationKind.Copy,
                        Sources = [source],
                        Destination = singleRename ? Path.GetDirectoryName(target)! : effectiveDestination,
                    },
                    conflictResolver,
                    state,
                    cancellationToken).ConfigureAwait(false);

                DeletePath(source, useRecycleBin: false);
                state.MovedCount++;
            }

            return;
        }

        await CopyAsync(
            request with { Kind = FileOperationKind.Copy, Destination = effectiveDestination },
            conflictResolver,
            state,
            cancellationToken).ConfigureAwait(false);

        foreach (string source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletePath(source, useRecycleBin: false);
            state.MovedCount++;
        }
    }

    private static void Delete(FileOperationRequest request, OperationState state, CancellationToken cancellationToken)
    {
        state.SetTotals(CalculateSourcesSize(request.Sources), request.Sources.Count);

        foreach (string path in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Report(path, null, 0, 0);
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    state.SkippedCount++;
                    state.CompleteItem();
                    continue;
                }

                DeletePath(path, request.Options.UseRecycleBinForDelete);
                state.DeletedCount++;
                state.CompleteItem();
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

    private static CopyPlan BuildCopyPlan(
        IReadOnlyList<string> sources,
        string destination,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken)
    {
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
                string target = Path.Combine(destination, fileName);
                if (PathsEqual(source, target))
                    throw new IOException("Source and destination file are the same.");
                if (!hasMask || matcher.IsMatch(options.FileMask!, fileName, groups))
                {
                    var snapshot = GetSourceSnapshot(source);
                    files.Add(new CopyFilePlanItem(source, target, snapshot.Length, snapshot.LastWriteTimeUtc));
                    plannedBytes += snapshot.Length;
                    state.ReportScanning(Path.GetDirectoryName(source) ?? source, files.Count, directories.Count, plannedBytes);
                }
                continue;
            }

            if (!Directory.Exists(source))
                continue;

            string rootDestination = Path.Combine(destination, Path.GetFileName(source));
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

    private static CopyDestination ResolveDestinationPath(
        CopyFilePlanItem file,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state,
        CancellationToken cancellationToken)
    {
        string destinationPath = file.DestinationPath;
        while (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            if (File.Exists(destinationPath) && options.OnlyNewer && !IsSourceNewer(file.SourcePath, destinationPath))
            {
                state.SkippedCount++;
                state.CompleteItem();
                return CopyDestination.Skip;
            }

            ConflictDecisionMode mode = ResolveConfiguredConflictDecision(options, state);
            string? newDestination = null;

            if (mode == ConflictDecisionMode.Ask)
            {
                var decision = conflictResolver.Resolve(BuildConflict(file.SourcePath, destinationPath));
                mode = decision.Mode;
                newDestination = decision.NewDestinationPath;
            }

            if (mode == ConflictDecisionMode.ResumeWithTailValidation)
            {
                CopyDestination? resumeDestination = TryResolveTailValidatedResume(file, destinationPath, options, state, cancellationToken);
                if (resumeDestination is not null)
                    return resumeDestination.Value;

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
                    File.Delete(destinationPath);
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
                case ConflictDecisionMode.Append:
                    if (Directory.Exists(destinationPath))
                        throw new IOException("Cannot append a file to a directory.");
                    return new CopyDestination(destinationPath, CopyDestinationAction.Append);
                case ConflictDecisionMode.OnlyNewer:
                    if (File.Exists(destinationPath) && IsSourceNewer(file.SourcePath, destinationPath))
                    {
                        File.Delete(destinationPath);
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
            ConflictDecisionMode.Append => ConflictDecisionMode.Append,
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
            case ConflictDecisionMode.AppendAll:
                state.StickyConflictDecision = ConflictDecisionMode.Append;
                return ConflictDecisionMode.Append;
            default:
                return mode;
        }
    }

    private static CopyDestination? TryResolveTailValidatedResume(
        CopyFilePlanItem file,
        string destinationPath,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken)
    {
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
        CopyResumePlan plan = ResumeAnalyzer.Analyze(file.SourcePath, destinationPath, snapshot, cancellationToken);
        if (plan.Kind != CopyResumePlanKind.CanResume)
        {
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

    private static async Task CopyFileContentsAsync(
        string sourcePath,
        string destinationPath,
        long size,
        CopyDestinationAction destinationAction,
        long resumeOffset,
        FileOperationOptions options,
        OperationState state,
        CancellationToken cancellationToken)
    {
        long currentBytes = destinationAction == CopyDestinationAction.ResumeWithTailValidation
            ? resumeOffset
            : 0;
        if (currentBytes > 0)
            state.AddBytes(currentBytes);

        state.Report(sourcePath, destinationPath, currentBytes, size);

        var sourceInfo = new FileInfo(sourcePath);
        FileAttributes sourceAttributes = sourceInfo.Attributes;

        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var destination = new FileStream(
                destinationPath,
                destinationAction == CopyDestinationAction.Append
                    ? FileMode.Append
                    : destinationAction == CopyDestinationAction.ResumeWithTailValidation
                        ? FileMode.Open
                        : FileMode.Create,
                destinationAction == CopyDestinationAction.ResumeWithTailValidation
                    ? FileAccess.ReadWrite
                    : FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[CopyBufferSize];
            if (destinationAction == CopyDestinationAction.ResumeWithTailValidation)
            {
                destination.SetLength(resumeOffset);
                source.Seek(resumeOffset, SeekOrigin.Begin);
                destination.Seek(resumeOffset, SeekOrigin.Begin);
            }

            while (true)
            {
                state.WaitIfPaused(cancellationToken);
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                currentBytes += read;
                state.AddBytes(read);
                state.Report(sourcePath, destinationPath, currentBytes, size);
            }
        }

        bool preserveMetadata = destinationAction != CopyDestinationAction.Append;
        if (preserveMetadata && options.PreserveTimestamps)
            PreserveFileTimestamps(sourceInfo, destinationPath, state);
        if (preserveMetadata && options.PreserveAttributes)
            File.SetAttributes(destinationPath, sourceAttributes);
        if (preserveMetadata && options.SecurityMode == FileSecurityMode.CopyAccessControl)
            TryCopyAccessControl(sourcePath, destinationPath, state);

        state.CopiedCount++;
        state.CompleteItem();
    }

    private static void CreateDirectoryTarget(
        string destinationPath,
        string sourcePath,
        FileOperationOptions options,
        OperationState state)
    {
        Directory.CreateDirectory(destinationPath);

        if (options.PreserveTimestamps)
        {
            var source = new DirectoryInfo(sourcePath);
            Directory.SetCreationTime(destinationPath, source.CreationTime);
            Directory.SetLastWriteTime(destinationPath, source.LastWriteTime);
            Directory.SetLastAccessTime(destinationPath, source.LastAccessTime);
        }

        if (options.PreserveAttributes)
            File.SetAttributes(destinationPath, File.GetAttributes(sourcePath));

        if (options.SecurityMode == FileSecurityMode.CopyAccessControl)
            TryCopyAccessControl(sourcePath, destinationPath, state);
    }

    private static bool TryMoveDirect(
        string source,
        string destination,
        FileOperationOptions options,
        IFileOperationConflictResolver conflictResolver,
        OperationState state)
    {
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
                case ConflictDecisionMode.OnlyNewer:
                    if (!sourceIsFile || !IsSourceNewer(source, destination))
                    {
                        state.SkippedCount++;
                        state.CompleteItem();
                        return true;
                    }
                    break;
                case ConflictDecisionMode.Rename:
                case ConflictDecisionMode.RenameAll:
                    destination = string.IsNullOrWhiteSpace(decision.NewDestinationPath)
                        ? GenerateName(destination)
                        : decision.NewDestinationPath;
                    break;
                case ConflictDecisionMode.ResumeWithTailValidation:
                    throw new InvalidOperationException("Tail-validated resume is only supported for copy operations.");
            }

            if (File.Exists(destination))
                File.Delete(destination);
            else if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
        }

        try
        {
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

    private static void CopyReparsePoint(
        string sourcePath,
        string destinationPath,
        FileOperationOptions options,
        OperationState state)
    {
        if (options.SymlinkMode == SymlinkCopyMode.CopyTargetContents)
            return;

        string? target = Directory.Exists(sourcePath)
            ? new DirectoryInfo(sourcePath).LinkTarget
            : new FileInfo(sourcePath).LinkTarget;

        if (string.IsNullOrWhiteSpace(target))
        {
            state.Errors.Add(new FileOperationItemError
            {
                Path = sourcePath,
                Message = "Cannot copy link because its target is unavailable.",
            });
            return;
        }

        if (Directory.Exists(sourcePath))
            Directory.CreateSymbolicLink(destinationPath, target);
        else
            File.CreateSymbolicLink(destinationPath, target);
    }

    private static void DeletePath(string path, bool useRecycleBin)
    {
        if (!useRecycleBin)
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static void PreserveFileTimestamps(FileInfo source, string destinationPath, OperationState state)
    {
        try
        {
            File.SetCreationTime(destinationPath, source.CreationTime);
            File.SetLastWriteTime(destinationPath, source.LastWriteTime);
            File.SetLastAccessTime(destinationPath, source.LastAccessTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.Errors.Add(new FileOperationItemError { Path = destinationPath, Message = ex.Message });
        }
    }

    private static void TryCopyAccessControl(string sourcePath, string destinationPath, OperationState state)
    {
        try
        {
            Type infoType = Directory.Exists(sourcePath) ? typeof(DirectoryInfo) : typeof(FileInfo);
            object sourceInfo = Directory.Exists(sourcePath) ? new DirectoryInfo(sourcePath) : new FileInfo(sourcePath);
            object destinationInfo = Directory.Exists(destinationPath) ? new DirectoryInfo(destinationPath) : new FileInfo(destinationPath);
            MethodInfo? getAccessControl = infoType.GetMethod("GetAccessControl", Type.EmptyTypes);
            MethodInfo? setAccessControl = infoType.GetMethods()
                .FirstOrDefault(m => m.Name == "SetAccessControl" && m.GetParameters().Length == 1);

            if (getAccessControl is null || setAccessControl is null)
                throw new PlatformNotSupportedException("Access control copy is not available in this runtime.");

            object? accessControl = getAccessControl.Invoke(sourceInfo, null);
            setAccessControl.Invoke(destinationInfo, [accessControl]);
        }
        catch (Exception ex) when (ex is TargetInvocationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            state.Errors.Add(new FileOperationItemError
            {
                Path = destinationPath,
                Message = ex.InnerException?.Message ?? ex.Message,
            });
        }
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

    private static long CalculateSourcesSize(IReadOnlyList<string> sources)
    {
        long total = 0;
        foreach (string source in sources)
        {
            if (File.Exists(source))
            {
                total += GetFileSize(source);
                continue;
            }

            if (Directory.Exists(source))
                total += Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories).Sum(GetFileSize);
        }
        return total;
    }

    private static long GetFileSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static CopyResumeSourceSnapshot GetSourceSnapshot(string path)
    {
        var source = new FileInfo(path);
        return new CopyResumeSourceSnapshot(source.Length, source.LastWriteTimeUtc);
    }

    private static bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return false;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
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

    private long CalculateProviderSourcesSize(
        IReadOnlyList<PanelLocation> sources,
        CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var location in sources)
            total += CalculateProviderSourceSize(location, cancellationToken);
        return total;
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

    private sealed record CopyFilePlanItem(string SourcePath, string DestinationPath, long Size, DateTime LastWriteTimeUtc);
    private sealed record CopyDirectoryPlanItem(string SourcePath, string DestinationPath);
    private enum CopyDestinationAction
    {
        CreateOrOverwrite,
        Append,
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

    private sealed class OperationState
    {
        private readonly FileOperationKind _kind;
        private readonly IProgress<FileOperationProgress>? _progress;
        private readonly IFileOperationPauseController? _pauseController;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Stopwatch _copyStopwatch = new();
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

        public void StartCopying()
        {
            if (!_copyStopwatch.IsRunning)
                _copyStopwatch.Start();
        }

        public void AddBytes(long bytes) => _bytesProcessed += bytes;

        public void CompleteItem() => _itemsDone++;

        public void CompleteFolder() => _foldersDone++;

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
                Elapsed = _copyStopwatch.Elapsed,
            });
        }

        public void Report(string currentPath, string? currentDestinationPath, long currentBytesDone, long currentBytesTotal)
        {
            double seconds = Math.Max(_copyStopwatch.Elapsed.TotalSeconds, 0.001);
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
                Elapsed = _copyStopwatch.Elapsed,
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
