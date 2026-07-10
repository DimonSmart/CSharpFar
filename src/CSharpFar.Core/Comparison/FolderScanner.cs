using CSharpFar.Core.FileMasks;

namespace CSharpFar.Core.Comparison;

public sealed class FolderScanner
{
    private readonly IComparisonFileSystem _fileSystem;
    private readonly FarMaskMatcher _maskMatcher = new();

    public FolderScanner(IComparisonFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new LocalComparisonFileSystem();
    }

    public IReadOnlyList<FileEntry> Scan(FolderScanRequest request, ComparisonOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootPath);

        var results = new List<FileEntry>();
        IReadOnlyList<string> roots = request.SelectedPaths.Count == 0
            ? [request.RootPath]
            : request.SelectedPaths;

        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanRoot(request.RootPath, root, options, results, cancellationToken);
        }

        return results;
    }

    private void ScanRoot(
        string compareRoot,
        string path,
        ComparisonOptions options,
        List<FileEntry> results,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_fileSystem.FileExists(path))
            {
                AddFile(compareRoot, path, options, results);
                return;
            }

            if (!_fileSystem.DirectoryExists(path))
            {
                results.Add(ErrorEntry(compareRoot, path, "Selected item does not exist."));
                return;
            }

            ScanDirectory(compareRoot, path, options, results, depth: 0, cancellationToken);
        }
        catch (Exception ex) when (IsFileSystemException(ex))
        {
            results.Add(ErrorEntry(compareRoot, path, ShortMessage(ex)));
        }
    }

    private void ScanDirectory(
        string compareRoot,
        string directory,
        ComparisonOptions options,
        List<FileEntry> results,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileAttributes directoryAttributes;
        try
        {
            directoryAttributes = _fileSystem.GetAttributes(directory);
        }
        catch (Exception ex) when (IsFileSystemException(ex))
        {
            results.Add(ErrorEntry(compareRoot, directory, ShortMessage(ex), isDirectory: true));
            return;
        }

        if (IsSymlink(directoryAttributes) && !PathsEqual(compareRoot, directory))
            return;

        IEnumerable<string> entries;
        try
        {
            entries = _fileSystem.EnumerateFileSystemEntries(directory).ToArray();
        }
        catch (Exception ex) when (IsFileSystemException(ex))
        {
            results.Add(ErrorEntry(compareRoot, directory, ShortMessage(ex), isDirectory: true));
            return;
        }

        foreach (string entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileAttributes attributes;
            try
            {
                attributes = _fileSystem.GetAttributes(entry);
            }
            catch (Exception ex) when (IsFileSystemException(ex))
            {
                results.Add(ErrorEntry(compareRoot, entry, ShortMessage(ex)));
                continue;
            }

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory)
            {
                if (IsExcludedPath(compareRoot, entry, options))
                    continue;
                if (!options.IncludeSubfolders || IsSymlink(attributes) || ReachedMaxDepth(depth, options.MaxDepth))
                    continue;
                ScanDirectory(compareRoot, entry, options, results, depth + 1, cancellationToken);
            }
            else
            {
                AddFile(compareRoot, entry, options, results, attributes);
            }
        }
    }

    private void AddFile(
        string compareRoot,
        string path,
        ComparisonOptions options,
        List<FileEntry> results,
        FileAttributes? knownAttributes = null)
    {
        string relativePath = RelativePath(compareRoot, path);
        if (!IsIncluded(relativePath, options) || IsExcluded(relativePath, Path.GetFileName(path), options))
            return;

        try
        {
            var attributes = knownAttributes ?? _fileSystem.GetAttributes(path);
            results.Add(new FileEntry
            {
                FullPath = path,
                RelativePath = relativePath,
                FileName = Path.GetFileName(path),
                Size = _fileSystem.GetFileSize(path),
                LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(path),
                IsDirectory = false,
                IsSymlink = IsSymlink(attributes),
            });
        }
        catch (Exception ex) when (IsFileSystemException(ex))
        {
            results.Add(ErrorEntry(compareRoot, path, ShortMessage(ex)));
        }
    }

    private bool IsIncluded(string relativePath, ComparisonOptions options)
    {
        string masks = string.IsNullOrWhiteSpace(options.IncludeMasks) ? "*" : options.IncludeMasks.Trim();
        return MaskMatches(masks, relativePath, Path.GetFileName(relativePath), options);
    }

    private bool IsExcludedPath(string compareRoot, string path, ComparisonOptions options) =>
        IsExcluded(RelativePath(compareRoot, path), Path.GetFileName(path), options);

    private bool IsExcluded(string relativePath, string fileName, ComparisonOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ExcludeMasks))
            return false;

        return MaskMatches(options.ExcludeMasks, relativePath, fileName, options);
    }

    private bool MaskMatches(string masks, string relativePath, string fileName, ComparisonOptions options)
    {
        bool caseSensitive = options.IsNameComparisonCaseSensitive;
        string normalized = NormalizeSeparators(relativePath);
        if (_maskMatcher.IsMatch(masks, normalized, new Dictionary<string, CSharpFar.Core.Highlighting.MaskGroup>(), caseSensitive))
            return true;

        return _maskMatcher.IsMatch(masks, fileName, new Dictionary<string, CSharpFar.Core.Highlighting.MaskGroup>(), caseSensitive);
    }

    private FileEntry ErrorEntry(string compareRoot, string path, string message, bool isDirectory = false) =>
        new()
        {
            FullPath = path,
            RelativePath = RelativePath(compareRoot, path),
            FileName = Path.GetFileName(path),
            IsDirectory = isDirectory,
            Error = message,
        };

    internal static string RelativePath(string rootPath, string path)
    {
        try
        {
            string relative = Path.GetRelativePath(rootPath, path);
            return relative == "." ? Path.GetFileName(path) : NormalizeSeparators(relative);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return NormalizeSeparators(Path.GetFileName(path));
        }
    }

    internal static string NormalizeSeparators(string path) =>
        path.Replace('\\', '/');

    private static bool ReachedMaxDepth(int currentDirectoryDepth, int? maxDepth) =>
        maxDepth.HasValue && currentDirectoryDepth >= maxDepth.Value;

    private static bool IsSymlink(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool IsFileSystemException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException or PathTooLongException or NotSupportedException;

    internal static string ShortMessage(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
}
