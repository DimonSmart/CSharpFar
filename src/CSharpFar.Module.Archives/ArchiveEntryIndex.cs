using CSharpFar.Core.Models;
using SharpCompress.Common;

namespace CSharpFar.Module.Archives;

internal sealed record ArchiveNode(
    string Path,
    bool IsDirectory,
    long Size,
    DateTime Modified,
    int EntryIndex,
    bool Explicit);

internal sealed class ArchiveEntryIndex
{
    internal const int MaxEntries = 100_000;
    internal const long MaxTotalSize = 32L * 1024 * 1024 * 1024;
    internal const long MaxSingleSize = 8L * 1024 * 1024 * 1024;

    private readonly Dictionary<string, ArchiveNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal)
    {
        ["/"] = [],
    };

    public ArchiveEntryIndex()
    {
        _nodes.Add("/", new ArchiveNode("/", true, 0, default, -1, false));
    }

    public void Add(IEntry entry, int index, bool rarPathSeparators = false)
    {
        if (index >= MaxEntries)
            throw new InvalidDataException($"Archive contains more than {MaxEntries:N0} entries.");
        if (entry.LinkTarget is not null)
            throw new NotSupportedException("Archives containing symbolic or hard links are not supported.");
        if (entry.IsEncrypted)
            throw new NotSupportedException("Encrypted or password-protected archive entries are not supported.");
        if (entry.IsSplitAfter || entry.VolumeIndexFirst != entry.VolumeIndexLast)
            throw new NotSupportedException("Multi-volume archives are not supported.");

        string path = ArchivePath.NormalizeEntry(entry.Key ?? "", rarPathSeparators);
        long size = entry.IsDirectory ? 0 : entry.Size;
        if (size < 0 || size > MaxSingleSize)
            throw new InvalidDataException("Archive entry exceeds the supported unpacked size limit.");
        if (!entry.IsDirectory && size > 64L * 1024 * 1024 &&
            entry.CompressedSize > 0 && size / entry.CompressedSize > 10_000)
            throw new InvalidDataException("Archive entry has an unsafe compression ratio.");

        EnsureDirectory(ArchivePath.Parent(path)!);

        if (_nodes.TryGetValue(path, out var existing))
        {
            if (existing.Explicit || !entry.IsDirectory)
                throw new InvalidDataException($"Duplicate or ambiguous archive entry: {path}");
            _nodes[path] = existing with { Modified = entry.LastModifiedTime ?? default, Explicit = true, EntryIndex = index };
            return;
        }

        _nodes[path] = new ArchiveNode(path, entry.IsDirectory, size,
            entry.LastModifiedTime ?? default, index, true);
        if (entry.IsDirectory)
            _children[path] = [];
        _children[ArchivePath.Parent(path)!].Add(path);

        checked { TotalSize += size; }
        if (TotalSize > MaxTotalSize)
            throw new InvalidDataException("Archive exceeds the total unpacked size limit.");
    }

    private void EnsureDirectory(string path)
    {
        if (_nodes.TryGetValue(path, out var existing))
        {
            if (!existing.IsDirectory)
                throw new InvalidDataException($"Archive entry has a file as its parent: {path}");
            return;
        }

        string parent = ArchivePath.Parent(path)!;
        EnsureDirectory(parent);
        _nodes.Add(path, new ArchiveNode(path, true, 0, default, -1, false));
        _children.Add(path, []);
        _children[parent].Add(path);
        if (_nodes.Count > MaxEntries * 2)
            throw new InvalidDataException("Archive has too many nested directories.");
    }

    public long TotalSize { get; private set; }

    public ArchiveNode? Find(string path) =>
        _nodes.GetValueOrDefault(ArchivePath.NormalizePanel(path));

    public IReadOnlyList<FilePanelItem> Children(string path, PanelSourceId sourceId)
    {
        path = ArchivePath.NormalizePanel(path);
        if (!_children.TryGetValue(path, out var paths))
            throw new DirectoryNotFoundException($"Archive directory not found: {path}");
        return paths.Select(p => ToItem(_nodes[p], sourceId)).ToArray();
    }

    public static FilePanelItem ToItem(ArchiveNode node, PanelSourceId sourceId) => new()
    {
        Name = node.Path[(node.Path.LastIndexOf('/') + 1)..],
        FullPath = node.Path,
        SourceId = sourceId,
        IsDirectory = node.IsDirectory,
        Size = node.IsDirectory ? null : node.Size,
        LastWriteTime = node.Modified,
        Attributes = node.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
    };
}
