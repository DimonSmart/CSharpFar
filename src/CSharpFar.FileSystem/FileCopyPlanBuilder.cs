using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

// The copy plan is independent of transfer strategy and is shared by Preview and execution.
internal sealed class FileCopyPlanBuilder(
    IFilePanelSourceRegistry sources,
    Action<PanelSourceId, IEnumerable<string>> validatePlanCollisions)
{
    private readonly IFilePanelSourceRegistry _sources = sources;
    private readonly Action<PanelSourceId, IEnumerable<string>> _validatePlanCollisions = validatePlanCollisions;

    public List<ProviderOperationPlanItem> Build(
        IReadOnlyList<PanelLocation> sources,
        PanelLocation destination,
        FileOperationOptions options,
        bool useDestinationTemplate,
        CancellationToken cancellationToken,
        Action<string, int, int, long>? reportScanning = null,
        string? rootTarget = null)
    {
        if (rootTarget is not null && sources.Count != 1)
            throw new ArgumentException("An exact copy target requires one source.", nameof(sources));

        DestinationPattern destinationPattern = useDestinationTemplate
            ? default
            : DestinationPattern.Parse(destination.SourcePath, destination.SourceId);
        DestinationTemplate? template = useDestinationTemplate ? DestinationTemplate.Parse(destination.SourcePath) : null;
        IFilePanelSource destinationSource = _sources.GetSource(destination.SourceId);
        IReadOnlyCollection<char>? destinationPathSeparators = template is null
            ? null
            : destinationSource.PathSeparators;
        var plan = new List<ProviderOperationPlanItem>(sources.Count);
        bool hasMask = !string.IsNullOrWhiteSpace(options.FileMask);
        var matcher = new FarMaskMatcher();
        IReadOnlyDictionary<string, MaskGroup> groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (PanelLocation location in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFilePanelSource source = _sources.GetSource(location.SourceId);
            FilePanelItem? item = source.GetItem(location.SourcePath, cancellationToken);
            string normalizedSourcePath = source.NormalizePath(location.SourcePath);
            if (item is null)
            {
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, null, string.Empty, destination.SourceId));
                continue;
            }

            string targetPath = rootTarget ?? (template is not null
                ? template.Evaluate(new DestinationTemplateContext(item.Name, item.IsDirectory, item.LastWriteTime, destinationPathSeparators!))
                : destinationPattern.HasWildcards
                    ? FileOperationService.CombineProviderPath(
                        destination.SourceId,
                        destinationPattern.ParentPath,
                        destinationPattern.TransformName(item.Name))
                    : ResolveProviderCopyTargetPath(
                        destinationSource,
                        item,
                        destination.SourcePath,
                        sources.Count,
                        cancellationToken));

            if (!hasMask)
            {
                if (location.SourceId == destination.SourceId)
                    ValidateCopyTarget(source, normalizedSourcePath, targetPath, item.IsDirectory);
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, item, targetPath, destination.SourceId));
                continue;
            }

            AddMaskedProviderPlanItems(
                plan, location, source, normalizedSourcePath, item, targetPath, options.FileMask!, options.SymlinkMode, matcher, groups,
                destination.SourceId,
                validateTarget: path =>
                {
                    if (location.SourceId == destination.SourceId)
                        ValidateCopyTarget(source, normalizedSourcePath, path, item.IsDirectory);
                },
                cancellationToken);
        }

        if (template is not null || destinationPattern.HasWildcards)
            _validatePlanCollisions(destination.SourceId, plan.Select(item => item.TargetPath));

        // Ordinary provider directory copy is fully discovered before any destination
        // mutation, including for archive batches and cross-provider moves.
        if (!hasMask)
        {
            int scannedFiles = 0;
            int scannedFolders = 0;
            long knownBytes = 0;
            var expanded = new List<ProviderOperationPlanItem>(plan.Count);
            foreach (ProviderOperationPlanItem root in plan)
            {
                var visiting = new HashSet<string>(
                    root.Location.SourceId == PanelSourceId.Local
                        ? LocalFileSystemPathComparer.Current : StringComparer.Ordinal);
                expanded.Add(ExpandProviderCopyItem(
                    root, visiting, options.SymlinkMode, reportScanning, ref scannedFiles, ref scannedFolders, ref knownBytes, cancellationToken));
            }
            return expanded;
        }
        return plan;
    }

    private static ProviderOperationPlanItem ExpandProviderCopyItem(
        ProviderOperationPlanItem item,
        HashSet<string> visiting,
        SymlinkCopyMode symlinkMode,
        Action<string, int, int, long>? reportScanning,
        ref int scannedFiles,
        ref int scannedFolders,
        ref long knownBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Item is not { } sourceItem)
            return item;

        bool isLink = IsLink(sourceItem);
        if (isLink && symlinkMode == SymlinkCopyMode.CopyLink)
        {
            scannedFiles++;
            reportScanning?.Invoke(item.SourcePath, scannedFiles, scannedFolders, knownBytes);
            // The preview and execution both see a single link, never its target tree.
            return item with { Mapping = item.Mapping with { IsDirectory = false } };
        }

        if (isLink && item.Source.SourceId != PanelSourceId.Local)
            throw new NotSupportedException(
                $"Provider '{item.Source.DisplayName}' cannot reliably resolve symbolic link targets.");

        if (!sourceItem.IsDirectory)
        {
            scannedFiles++;
            knownBytes = checked(knownBytes + (sourceItem.Size ?? 0));
            reportScanning?.Invoke(item.SourcePath, scannedFiles, scannedFolders, knownBytes);
            return item;
        }

        string identity = GetDirectoryIdentity(item.Source, item.SourcePath);
        if (!visiting.Add(identity))
            throw new IOException($"Cyclic directory traversal through '{item.SourcePath}' (target '{identity}').");

        try
        {
            scannedFolders++;
            reportScanning?.Invoke(item.SourcePath, scannedFiles, scannedFolders, knownBytes);
            var children = new List<ProviderOperationPlanItem>();
            foreach (FilePanelItem child in item.Source.EnumerateDirectory(item.SourcePath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.IsParentDirectory)
                    continue;
                string targetPath = FileOperationService.CombineProviderPath(
                    item.Mapping.Destination.SourceId, item.TargetPath, child.Name);
                var childPlan = new ProviderOperationPlanItem(
                    child.Location, item.Source, item.Source.NormalizePath(child.SourcePath),
                    child, targetPath, item.Mapping.Destination.SourceId);
                children.Add(ExpandProviderCopyItem(
                    childPlan, visiting, symlinkMode, reportScanning, ref scannedFiles, ref scannedFolders, ref knownBytes,
                    cancellationToken));
            }
            return item with { Children = children };
        }
        finally
        {
            visiting.Remove(identity);
        }
    }

    internal static void AddMaskedProviderPlanItems(
        List<ProviderOperationPlanItem> plan,
        PanelLocation rootLocation,
        IFilePanelSource source,
        string rootSourcePath,
        FilePanelItem rootItem,
        string rootTargetPath,
        string fileMask,
        SymlinkCopyMode symlinkMode,
        FarMaskMatcher matcher,
        IReadOnlyDictionary<string, MaskGroup> groups,
        PanelSourceId destinationSourceId,
        Action<string> validateTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!rootItem.IsDirectory || (IsLink(rootItem) && symlinkMode == SymlinkCopyMode.CopyLink))
        {
            if (matcher.IsMatch(fileMask, rootItem.Name, groups))
            {
                validateTarget(rootTargetPath);
                var linkPlan = new ProviderOperationPlanItem(
                    rootLocation, source, rootSourcePath, rootItem, rootTargetPath, destinationSourceId);
                plan.Add(IsLink(rootItem) && symlinkMode == SymlinkCopyMode.CopyLink
                    ? linkPlan with { Mapping = linkPlan.Mapping with { IsDirectory = false } }
                    : linkPlan);
            }
            return;
        }

        var files = new List<FilePanelItem>();
        var visiting = new HashSet<string>(
            source.SourceId == PanelSourceId.Local ? LocalFileSystemPathComparer.Current : StringComparer.Ordinal);
        CollectMatchingProviderFiles(source, rootSourcePath, fileMask, symlinkMode, matcher, groups, files, visiting, cancellationToken);
        if (files.Count == 0)
            return;

        validateTarget(rootTargetPath);
        foreach (FilePanelItem file in files)
        {
            string sourcePath = source.NormalizePath(file.SourcePath);
            string relativePath = GetProviderRelativePath(source.SourceId, rootSourcePath, sourcePath);
            string targetPath = CombineProviderRelativePath(destinationSourceId, rootTargetPath, relativePath);
            var filePlan = new ProviderOperationPlanItem(file.Location, source, sourcePath, file, targetPath, destinationSourceId);
            plan.Add(IsLink(file) && symlinkMode == SymlinkCopyMode.CopyLink
                ? filePlan with { Mapping = filePlan.Mapping with { IsDirectory = false } }
                : filePlan);
        }
    }

    private static void CollectMatchingProviderFiles(
        IFilePanelSource source,
        string directoryPath,
        string fileMask,
        SymlinkCopyMode symlinkMode,
        FarMaskMatcher matcher,
        IReadOnlyDictionary<string, MaskGroup> groups,
        List<FilePanelItem> files,
        HashSet<string> visiting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string identity = GetDirectoryIdentity(source, directoryPath);
        if (!visiting.Add(identity))
            throw new IOException($"Cyclic masked directory traversal through '{directoryPath}' (target '{identity}').");

        try
        {
            foreach (FilePanelItem child in source.EnumerateDirectory(directoryPath, cancellationToken)
                         .Where(item => !item.IsParentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool isLink = IsLink(child);
                if (isLink && symlinkMode == SymlinkCopyMode.CopyLink)
                {
                    if (matcher.IsMatch(fileMask, child.Name, groups))
                        files.Add(child);
                }
                else if (isLink && source.SourceId != PanelSourceId.Local)
                {
                    throw new NotSupportedException(
                        $"Provider '{source.DisplayName}' cannot reliably resolve symbolic link targets.");
                }
                else if (child.IsDirectory)
                {
                    CollectMatchingProviderFiles(source, child.SourcePath, fileMask,
                        symlinkMode, matcher, groups, files, visiting, cancellationToken);
                }
                else if (matcher.IsMatch(fileMask, child.Name, groups))
                {
                    files.Add(child);
                }
            }
        }
        finally { visiting.Remove(identity); }
    }

    private static bool IsLink(FilePanelItem item) =>
        (item.Attributes & FileAttributes.ReparsePoint) != 0;

    private static string GetDirectoryIdentity(IFilePanelSource source, string path)
    {
        if (source.SourceId != PanelSourceId.Local)
            return source.NormalizePath(path);

        // Resolve every symlinked path component: resolving only the final item
        // misses aliases such as /root/link/child where child itself is ordinary.
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)
            ?? throw new IOException($"Cannot determine filesystem root for '{path}'.");
        string current = root;
        foreach (string segment in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
            {
                FileSystemInfo resolved = directory.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException($"Cannot resolve symbolic link '{current}'.");
                current = Path.GetFullPath(resolved.FullName);
            }
            if (!Directory.Exists(current))
                throw new IOException($"Cannot traverse broken or inaccessible directory link '{current}'.");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static string GetProviderRelativePath(PanelSourceId sourceId, string rootPath, string childPath)
    {
        if (sourceId == PanelSourceId.Local)
            return Path.GetRelativePath(rootPath, childPath).Replace(Path.DirectorySeparatorChar, '/');

        string normalizedRoot = rootPath.TrimEnd('/');
        return childPath[(normalizedRoot.Length + 1)..];
    }

    private static string CombineProviderRelativePath(PanelSourceId sourceId, string rootPath, string relativePath) =>
        sourceId == PanelSourceId.Local
            ? Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))
            : rootPath.TrimEnd('/') + "/" + relativePath;

    internal static void ValidateCopyTarget(
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

    private static string ResolveProviderCopyTargetPath(
        IFilePanelSource destinationSource,
        FilePanelItem item,
        string destinationPath,
        int sourceCount,
        CancellationToken cancellationToken)
    {
        if (sourceCount > 1 || item.IsDirectory)
            return FileOperationService.CombineProviderPath(destinationSource.SourceId, destinationPath, item.Name);

        if (destinationSource.SourceId == PanelSourceId.Local && !FileOperationService.IsPath(destinationPath))
            return FileOperationService.CombineProviderPath(destinationSource.SourceId, destinationPath, item.Name);

        if (destinationPath.Length == 0 || destinationSource.PathSeparators.Contains(destinationPath[^1]))
            return FileOperationService.CombineProviderPath(destinationSource.SourceId, destinationPath, item.Name);

        FilePanelItem? destinationItem = destinationSource.GetItem(destinationPath, cancellationToken);
        return destinationItem?.IsDirectory == true
            ? FileOperationService.CombineProviderPath(destinationSource.SourceId, destinationPath, item.Name)
            : destinationPath;
    }

}

internal sealed record ProviderOperationPlanItem(FileOperationPlanItem Mapping, IFilePanelSource Source, FilePanelItem? Item)
{
    public ProviderOperationPlanItem(
        PanelLocation sourceLocation,
        IFilePanelSource source,
        string sourcePath,
        FilePanelItem? item,
        string targetPath,
        PanelSourceId destinationSourceId)
        : this(
            new FileOperationPlanItem(
                sourceLocation with { SourcePath = sourcePath },
                new PanelLocation(destinationSourceId, targetPath),
                item?.IsDirectory ?? false),
            source,
            item) { }

    public PanelLocation Location => Mapping.Source;
    public string SourcePath => Mapping.Source.SourcePath;
    public string TargetPath => Mapping.Destination.SourcePath;
    public IReadOnlyList<ProviderOperationPlanItem> Children { get; init; } = [];
}

