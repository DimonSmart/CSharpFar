using System.Runtime.InteropServices;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem.Platform;

namespace CSharpFar.FileSystem;

public sealed class FileMetadataService : IFileMetadataService
{
    private readonly IFileMetadataProvider _provider;

    public FileMetadataService()
        : this(CreateDefaultProvider())
    {
    }

    internal FileMetadataService(IFileMetadataProvider provider)
    {
        _provider = provider;
    }

    public FileMetadataSnapshot GetMetadata(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        bool isDirectory = (attributes & FileAttributes.Directory) != 0;

        return new FileMetadataSnapshot(
            Path: path,
            DisplayName: System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : path,
            IsDirectory: isDirectory,
            Attributes: attributes,
            CreationTime: SafeGet(() => isDirectory ? Directory.GetCreationTime(path) : File.GetCreationTime(path)),
            LastWriteTime: SafeGet(() => isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path)),
            LastAccessTime: SafeGet(() => isDirectory ? Directory.GetLastAccessTime(path) : File.GetLastAccessTime(path)),
            OwnerDisplayName: SafeGet(() => _provider.GetOwnerDisplayName(path)),
            AttributesDescriptors: _provider.GetAttributeDescriptors(path, attributes).Where(static d => d.IsVisible).ToList(),
            AttributeStates: AttributeStates(attributes),
            CanEditCreationTime: _provider.CanEditCreationTime(path, attributes),
            CanEditLastWriteTime: _provider.CanEditLastWriteTime(path, attributes),
            CanEditLastAccessTime: _provider.CanEditLastAccessTime(path, attributes));
    }

    public FileMetadataSnapshot GetMergedMetadata(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one path is required.", nameof(paths));

        var snapshots = paths.Select(GetMetadata).ToList();
        var first = snapshots[0];
        var descriptorIds = snapshots
            .SelectMany(static snapshot => snapshot.AttributesDescriptors)
            .GroupBy(static descriptor => descriptor.Id)
            .Select(static group => group.First())
            .ToList();

        return first with
        {
            Path = first.Path,
            DisplayName = paths.Count == 1 ? first.DisplayName : $"{paths.Count} items",
            IsDirectory = snapshots.All(static snapshot => snapshot.IsDirectory),
            Attributes = MergeAttributes(snapshots),
            CreationTime = SameOrNull(snapshots.Select(static snapshot => snapshot.CreationTime)),
            LastWriteTime = SameOrNull(snapshots.Select(static snapshot => snapshot.LastWriteTime)),
            LastAccessTime = SameOrNull(snapshots.Select(static snapshot => snapshot.LastAccessTime)),
            OwnerDisplayName = SameOrNull(snapshots.Select(static snapshot => snapshot.OwnerDisplayName)),
            AttributesDescriptors = descriptorIds,
            AttributeStates = MergedAttributeStates(snapshots),
            CanEditCreationTime = snapshots.All(static snapshot => snapshot.CanEditCreationTime),
            CanEditLastWriteTime = snapshots.All(static snapshot => snapshot.CanEditLastWriteTime),
            CanEditLastAccessTime = snapshots.All(static snapshot => snapshot.CanEditLastAccessTime),
        };
    }

    public FileMetadataApplyResult ApplyMetadata(
        IReadOnlyList<string> paths,
        FileMetadataChangeSet changes)
    {
        var errors = new List<FileMetadataApplyError>();
        int changed = 0;

        foreach (string path in paths)
        {
            bool itemChanged = false;
            FileAttributes attributes;
            bool isDirectory;
            try
            {
                attributes = File.GetAttributes(path);
                isDirectory = (attributes & FileAttributes.Directory) != 0;
            }
            catch (Exception ex)
            {
                errors.Add(new FileMetadataApplyError(path, "Read metadata", ex.Message, ex));
                continue;
            }

            if (changes.AttributeChanges.Count > 0)
            {
                try
                {
                    _provider.ApplyAttributes(path, attributes, changes.AttributeChanges);
                    itemChanged = true;
                }
                catch (Exception ex)
                {
                    errors.Add(new FileMetadataApplyError(path, "Set attributes", ex.Message, ex));
                }
            }

            itemChanged |= TrySetTime(path, isDirectory, "Creation time", changes.CreationTime, SetCreationTime, errors);
            itemChanged |= TrySetTime(path, isDirectory, "Last write time", changes.LastWriteTime, SetLastWriteTime, errors);
            itemChanged |= TrySetTime(path, isDirectory, "Last access time", changes.LastAccessTime, SetLastAccessTime, errors);

            if (itemChanged)
                changed++;
        }

        return new FileMetadataApplyResult(paths.Count, changed, errors);
    }

    public void OpenSystemProperties(string path) => _provider.OpenSystemProperties(path);
    public bool CanOpenSystemProperties => _provider.CanOpenSystemProperties;

    private static bool TrySetTime(
        string path,
        bool isDirectory,
        string operation,
        DateTime? value,
        Action<string, bool, DateTime> setter,
        List<FileMetadataApplyError> errors)
    {
        if (value is null)
            return false;

        try
        {
            setter(path, isDirectory, value.Value);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(new FileMetadataApplyError(path, operation, ex.Message, ex));
            return false;
        }
    }

    private static void SetCreationTime(string path, bool isDirectory, DateTime value)
    {
        if (isDirectory)
            Directory.SetCreationTime(path, value);
        else
            File.SetCreationTime(path, value);
    }

    private static void SetLastWriteTime(string path, bool isDirectory, DateTime value)
    {
        if (isDirectory)
            Directory.SetLastWriteTime(path, value);
        else
            File.SetLastWriteTime(path, value);
    }

    private static void SetLastAccessTime(string path, bool isDirectory, DateTime value)
    {
        if (isDirectory)
            Directory.SetLastAccessTime(path, value);
        else
            File.SetLastAccessTime(path, value);
    }

    private static FileAttributes MergeAttributes(IReadOnlyList<FileMetadataSnapshot> snapshots)
    {
        FileAttributes merged = 0;
        foreach (FileAttributeId id in Enum.GetValues<FileAttributeId>())
        {
            bool allHave = snapshots.All(snapshot => FileAttributeMapping.Has(snapshot.Attributes, id));
            if (allHave)
                merged |= FileAttributeMapping.ToFileAttributes(id);
        }

        return merged;
    }

    private static IReadOnlyDictionary<FileAttributeId, AttributeEditState> AttributeStates(FileAttributes attributes) =>
        Enum.GetValues<FileAttributeId>()
            .ToDictionary(
                static id => id,
                id => FileAttributeMapping.Has(attributes, id) ? AttributeEditState.Checked : AttributeEditState.Unchecked);

    private static IReadOnlyDictionary<FileAttributeId, AttributeEditState> MergedAttributeStates(IReadOnlyList<FileMetadataSnapshot> snapshots) =>
        Enum.GetValues<FileAttributeId>()
            .ToDictionary(
                static id => id,
                id =>
                {
                    bool any = snapshots.Any(snapshot => snapshot.AttributeStates[id] == AttributeEditState.Checked);
                    bool all = snapshots.All(snapshot => snapshot.AttributeStates[id] == AttributeEditState.Checked);
                    return all
                        ? AttributeEditState.Checked
                        : any
                            ? AttributeEditState.Indeterminate
                            : AttributeEditState.Unchecked;
                });

    private static T? SameOrNull<T>(IEnumerable<T?> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
            return default;

        T? first = enumerator.Current;
        var comparer = EqualityComparer<T?>.Default;
        while (enumerator.MoveNext())
        {
            if (!comparer.Equals(first, enumerator.Current))
                return default;
        }

        return first;
    }

    private static T? SafeGet<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }

    private static IFileMetadataProvider CreateDefaultProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsFileMetadataProvider();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOsFileMetadataProvider();
        return new UnixFileMetadataProvider();
    }
}
