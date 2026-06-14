using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem.Platform;

internal static class FileAttributeMapping
{
    public static FileAttributes ToFileAttributes(FileAttributeId id) =>
        id switch
        {
            FileAttributeId.ReadOnly => FileAttributes.ReadOnly,
            FileAttributeId.Hidden => FileAttributes.Hidden,
            FileAttributeId.System => FileAttributes.System,
            FileAttributeId.Archive => FileAttributes.Archive,
            FileAttributeId.Temporary => FileAttributes.Temporary,
            FileAttributeId.NotContentIndexed => FileAttributes.NotContentIndexed,
            FileAttributeId.Compressed => FileAttributes.Compressed,
            FileAttributeId.Encrypted => FileAttributes.Encrypted,
            FileAttributeId.SparseFile => FileAttributes.SparseFile,
            FileAttributeId.Offline => FileAttributes.Offline,
            FileAttributeId.ReparsePoint => FileAttributes.ReparsePoint,
            FileAttributeId.Directory => FileAttributes.Directory,
            _ => 0,
        };

    public static bool Has(FileAttributes attributes, FileAttributeId id) =>
        (attributes & ToFileAttributes(id)) != 0;
}
