using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFilePanelSource
{
    PanelSourceId SourceId { get; }
    string DisplayName { get; }
    PanelProviderCapabilities Capabilities { get; }
    IReadOnlyCollection<char> PathSeparators { get; }

    // The comparer applies to normalized paths and must stay stable for this source.
    // Unknown provider semantics default to ordinal equality to avoid merging distinct files.
    StringComparer PathComparer => StringComparer.Ordinal;

    string NormalizePath(string sourcePath);

    string CombinePath(string parent, string name)
    {
        string directory = NormalizePath(parent);
        char separator = PathSeparators.First();
        return directory.Length != 0 && PathSeparators.Contains(directory[^1])
            ? directory + name
            : directory + separator + name;
    }

    string GetFileName(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        if (IsRootPath(normalized))
            return string.Empty;
        char[] separators = PathSeparators.ToArray();
        string trimmed = normalized.TrimEnd(separators);
        int index = trimmed.LastIndexOfAny(separators);
        return trimmed[(index + 1)..];
    }

    // Components, not a slash-delimited string: a destination provider may have
    // entirely different separators and a source name may contain a literal slash.
    IReadOnlyList<string> GetRelativePathSegments(string root, string path)
    {
        string normalizedRoot = NormalizePath(root);
        string current = NormalizePath(path);
        var segments = new Stack<string>();
        var visited = new HashSet<string>(PathComparer);
        while (!PathComparer.Equals(normalizedRoot, current))
        {
            if (!visited.Add(current) || IsRootPath(current))
                throw new ArgumentException("Path is not a descendant of the supplied root.", nameof(path));
            string name = GetFileName(current);
            string? parent = GetParentPath(current);
            if (parent is null || name.Length == 0)
                throw new ArgumentException("Path is not a descendant of the supplied root.", nameof(path));
            segments.Push(name);
            current = NormalizePath(parent);
        }

        return segments.ToArray();
    }

    bool IsRootPath(string sourcePath);
    string? GetParentPath(string sourcePath);

    IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default);

    FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default);
}
