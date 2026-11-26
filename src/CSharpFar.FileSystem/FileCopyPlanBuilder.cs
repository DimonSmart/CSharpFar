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

    public async Task<List<ProviderOperationPlanItem>> BuildAsync(
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

        IFilePanelSource destinationSource = _sources.GetSource(destination.SourceId);
        DestinationPattern destinationPattern = useDestinationTemplate
            ? default
            : DestinationPattern.Parse(destination.SourcePath, destinationSource);
        DestinationTemplate? template = useDestinationTemplate ? DestinationTemplate.Parse(destination.SourcePath) : null;
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
            FilePanelItem? item = await source.AsAsync().GetItemAsync(location.SourcePath, cancellationToken).ConfigureAwait(false);
            string normalizedSourcePath = source.NormalizePath(location.SourcePath);
            if (item is null)
            {
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, null, string.Empty, destination.SourceId));
                continue;
            }

            string targetPath = rootTarget ?? (template is not null
                ? template.Evaluate(new DestinationTemplateContext(item.Name, item.IsDirectory, item.LastWriteTime, destinationPathSeparators!))
                : destinationPattern.HasWildcards
                    ? destinationSource.CombinePath(
                        destinationPattern.ParentPath,
                        destinationPattern.TransformName(item.Name))
                    : await ResolveProviderCopyTargetPathAsync(
                        destinationSource,
                        item,
                        destination.SourceId == PanelSourceId.Local
                            ? FileOperationService.ResolveLocalDestinationPath(destination.SourcePath, location)
                            : destination.SourcePath,
                        sources.Count,
                        cancellationToken).ConfigureAwait(false));

            if (destination.SourceId == PanelSourceId.Local)
                targetPath = FileOperationService.ResolveLocalDestinationPath(targetPath, location);

            if (!hasMask)
            {
                if (location.SourceId == destination.SourceId)
                    ValidateCopyTarget(source, normalizedSourcePath, targetPath, item.IsDirectory);
                plan.Add(new ProviderOperationPlanItem(location, source, normalizedSourcePath, item, targetPath, destination.SourceId));
                continue;
            }

            await AddMaskedProviderPlanItemsAsync(
                plan, location, source, normalizedSourcePath, item, targetPath, options.FileMask!, options.SymlinkMode, matcher, groups,
                destinationSource,
                validateTarget: path =>
                {
                    if (location.SourceId == destination.SourceId)
                        ValidateCopyTarget(source, normalizedSourcePath, path, item.IsDirectory);
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (template is not null || destinationPattern.HasWildcards)
            _validatePlanCollisions(destination.SourceId, plan.Select(item => item.TargetPath));

        // Ordinary provider directory copy is fully discovered before any destination
        // mutation, including for archive batches and cross-provider moves.
        if (!hasMask)
        {
            var counts = new ScanCounts();
            var expanded = new List<ProviderOperationPlanItem>(plan.Count);
            foreach (ProviderOperationPlanItem root in plan)
            {
                var visiting = new HashSet<string>(root.Source.PathComparer);
                expanded.Add(await ExpandProviderCopyItemAsync(
                    root, destinationSource, visiting, options.SymlinkMode, reportScanning, counts, cancellationToken).ConfigureAwait(false));
            }
            return expanded;
        }
        return plan;
    }

    private static async Task<ProviderOperationPlanItem> ExpandProviderCopyItemAsync(
        ProviderOperationPlanItem item,
        IFilePanelSource destinationSource,
        HashSet<string> visiting,
        SymlinkCopyMode symlinkMode,
        Action<string, int, int, long>? reportScanning,
        ScanCounts counts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Item is not { } sourceItem)
            return item;

        bool isLink = IsLink(sourceItem);
        if (isLink && symlinkMode == SymlinkCopyMode.CopyLink)
        {
            counts.Files++;
            reportScanning?.Invoke(item.SourcePath, counts.Files, counts.Folders, counts.Bytes);
            // The preview and execution both see a single link, never its target tree.
            return item with { Mapping = item.Mapping with { IsDirectory = false } };
        }

        if (isLink && item.Source.SourceId != PanelSourceId.Local)
            throw new NotSupportedException(
                $"Provider '{item.Source.DisplayName}' cannot reliably resolve symbolic link targets.");
        if (isLink && symlinkMode == SymlinkCopyMode.CopyTargetContents)
            ValidateResolvedLink(item.SourcePath);

        if (!sourceItem.IsDirectory)
        {
            counts.Files++;
            counts.Bytes = checked(counts.Bytes + (sourceItem.Size ?? 0));
            reportScanning?.Invoke(item.SourcePath, counts.Files, counts.Folders, counts.Bytes);
            return item;
        }

        string identity = GetDirectoryIdentity(item.Source, item.SourcePath);
        if (!visiting.Add(identity))
            throw new IOException($"Cyclic directory traversal through '{item.SourcePath}' (target '{identity}').");

        try
        {
            counts.Folders++;
            reportScanning?.Invoke(item.SourcePath, counts.Files, counts.Folders, counts.Bytes);
            var children = new List<ProviderOperationPlanItem>();
            foreach (FilePanelItem child in (await item.Source.AsAsync().EnumerateDirectoryAsync(item.SourcePath, cancellationToken).ConfigureAwait(false)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.IsParentDirectory)
                    continue;
                string targetPath = destinationSource.CombinePath(item.TargetPath, child.Name);
                var childPlan = new ProviderOperationPlanItem(
                    child.Location, item.Source, item.Source.NormalizePath(child.SourcePath),
                    child, targetPath, item.Mapping.Destination.SourceId);
                children.Add(await ExpandProviderCopyItemAsync(
                    childPlan, destinationSource, visiting, symlinkMode, reportScanning, counts,
                    cancellationToken).ConfigureAwait(false));
            }
            return item with { Children = children };
        }
        finally
        {
            visiting.Remove(identity);
        }
    }

    private sealed class ScanCounts { public int Files; public int Folders; public long Bytes; }

    internal static async Task AddMaskedProviderPlanItemsAsync(
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
        IFilePanelSource destinationSource,
        Action<string> validateTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsLink(rootItem) && symlinkMode == SymlinkCopyMode.CopyTargetContents)
        {
            if (source.SourceId != PanelSourceId.Local)
                throw new NotSupportedException($"Provider '{source.DisplayName}' cannot resolve symbolic links.");
            ValidateResolvedLink(rootSourcePath);
        }

        if (!rootItem.IsDirectory || (IsLink(rootItem) && symlinkMode == SymlinkCopyMode.CopyLink))
        {
            if (matcher.IsMatch(fileMask, rootItem.Name, groups))
            {
                validateTarget(rootTargetPath);
                var linkPlan = new ProviderOperationPlanItem(
                    rootLocation, source, rootSourcePath, rootItem, rootTargetPath, destinationSource.SourceId);
                plan.Add(IsLink(rootItem) && symlinkMode == SymlinkCopyMode.CopyLink
                    ? linkPlan with { Mapping = linkPlan.Mapping with { IsDirectory = false } }
                    : linkPlan);
            }
            return;
        }

        var files = new List<FilePanelItem>();
        var visiting = new HashSet<string>(source.PathComparer);
        await CollectMatchingProviderFilesAsync(source, rootSourcePath, fileMask, symlinkMode, matcher, groups, files, visiting, cancellationToken).ConfigureAwait(false);
        if (files.Count == 0)
            return;

        validateTarget(rootTargetPath);
        foreach (FilePanelItem file in files)
        {
            string sourcePath = source.NormalizePath(file.SourcePath);
            string targetPath = rootTargetPath;
            foreach (string segment in source.GetRelativePathSegments(rootSourcePath, sourcePath))
                targetPath = destinationSource.CombinePath(targetPath, segment);
            var filePlan = new ProviderOperationPlanItem(file.Location, source, sourcePath, file, targetPath, destinationSource.SourceId);
            plan.Add(IsLink(file) && symlinkMode == SymlinkCopyMode.CopyLink
                ? filePlan with { Mapping = filePlan.Mapping with { IsDirectory = false } }
                : filePlan);
        }
    }

    private static async Task CollectMatchingProviderFilesAsync(
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
            foreach (FilePanelItem child in (await source.AsAsync().EnumerateDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(false))
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
                    if (isLink) ValidateResolvedLink(child.SourcePath);
                    await CollectMatchingProviderFilesAsync(source, child.SourcePath, fileMask,
                        symlinkMode, matcher, groups, files, visiting, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (isLink) ValidateResolvedLink(child.SourcePath);
                    if (matcher.IsMatch(fileMask, child.Name, groups))
                        files.Add(child);
                }
            }
        }
        finally { visiting.Remove(identity); }
    }

    private static bool IsLink(FilePanelItem item) =>
        (item.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void ValidateResolvedLink(string path)
    {
        // Validate all links, including ones whose Directory attribute cannot be
        // determined (e.g. A -> B -> C -> A). They must never disappear from
        // the preview or turn into a late read failure after destination creation.
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        FileSystemInfo? resolved = file.LinkTarget is not null
            ? file.ResolveLinkTarget(returnFinalTarget: true)
            : directory.ResolveLinkTarget(returnFinalTarget: true);
        if (resolved is null ||
            (!File.Exists(resolved.FullName) && !Directory.Exists(resolved.FullName)))
            throw new IOException($"Cannot resolve symbolic link target: '{path}'.");
    }

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

    private static async Task<string> ResolveProviderCopyTargetPathAsync(
        IFilePanelSource destinationSource,
        FilePanelItem item,
        string destinationPath,
        int sourceCount,
        CancellationToken cancellationToken)
    {
        if (sourceCount > 1 || item.IsDirectory)
            return destinationSource.CombinePath(destinationPath, item.Name);

        if (destinationPath.Length == 0 || destinationSource.PathSeparators.Contains(destinationPath[^1]))
            return destinationSource.CombinePath(destinationPath, item.Name);

        FilePanelItem? destinationItem = await destinationSource.AsAsync().GetItemAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        return destinationItem?.IsDirectory == true
            ? destinationSource.CombinePath(destinationPath, item.Name)
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

