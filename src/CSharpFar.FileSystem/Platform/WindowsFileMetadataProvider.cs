using System.Diagnostics;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem.Platform;

internal sealed class WindowsFileMetadataProvider : IFileMetadataProvider
{
    private static readonly FileAttributeId[] Editable =
    [
        FileAttributeId.ReadOnly,
        FileAttributeId.Hidden,
        FileAttributeId.System,
        FileAttributeId.Archive,
        FileAttributeId.Temporary,
        FileAttributeId.NotContentIndexed,
    ];

    private static readonly FileAttributeId[] VisibleReadOnly =
    [
        FileAttributeId.Directory,
        FileAttributeId.ReparsePoint,
        FileAttributeId.Compressed,
        FileAttributeId.Encrypted,
        FileAttributeId.SparseFile,
        FileAttributeId.Offline,
    ];

    public bool CanOpenSystemProperties => true;

    public IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes)
    {
        var descriptors = new List<FileAttributeDescriptor>();
        descriptors.AddRange(Editable.Select(id => Descriptor(id, editable: true)));
        descriptors.AddRange(VisibleReadOnly
            .Where(id => FileAttributeMapping.Has(attributes, id))
            .Select(id => Descriptor(id, editable: false, "Managed attributes cannot change this flag directly.")));
        return descriptors;
    }

    public bool CanEditCreationTime(string path, FileAttributes attributes) => true;
    public bool CanEditLastWriteTime(string path, FileAttributes attributes) => true;
    public bool CanEditLastAccessTime(string path, FileAttributes attributes) => true;

    public string? GetOwnerDisplayName(string path) => null;
    public UnixFileMetadata? GetUnixMetadata(string path, FileAttributes attributes) => null;

    public void ApplyAttributes(
        string path,
        FileAttributes currentAttributes,
        IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes)
    {
        FileAttributes next = currentAttributes;
        foreach (var id in Editable)
        {
            if (!changes.TryGetValue(id, out var state) || state == AttributeEditState.Indeterminate)
                continue;

            var flag = FileAttributeMapping.ToFileAttributes(id);
            next = state == AttributeEditState.Checked ? next | flag : next & ~flag;
        }

        if (next != currentAttributes)
            File.SetAttributes(path, next);
    }

    public void OpenSystemProperties(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "properties",
        });

    public void ApplyUnixPermissions(
        string path,
        UnixFileMetadata currentMetadata,
        IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> changes) =>
        throw new PlatformNotSupportedException("Unix permissions are not supported on Windows.");

    private static FileAttributeDescriptor Descriptor(
        FileAttributeId id,
        bool editable,
        string? disabledReason = null) =>
        new(id, Label(id), HotKey(id), IsVisible: true, IsEditable: editable, disabledReason);

    private static string Label(FileAttributeId id) =>
        id switch
        {
            FileAttributeId.ReadOnly => "Read only",
            FileAttributeId.NotContentIndexed => "Not indexed",
            FileAttributeId.SparseFile => "Sparse",
            FileAttributeId.ReparsePoint => "Reparse point",
            _ => id.ToString(),
        };

    private static char? HotKey(FileAttributeId id) =>
        id switch
        {
            FileAttributeId.ReadOnly => 'R',
            FileAttributeId.Hidden => 'H',
            FileAttributeId.System => 'S',
            FileAttributeId.Archive => 'A',
            FileAttributeId.Temporary => 'T',
            FileAttributeId.NotContentIndexed => 'N',
            _ => null,
        };
}
