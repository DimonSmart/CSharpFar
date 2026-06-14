using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem.Platform;

internal interface IFileMetadataProvider
{
    IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes);
    bool CanEditCreationTime(string path, FileAttributes attributes);
    bool CanEditLastWriteTime(string path, FileAttributes attributes);
    bool CanEditLastAccessTime(string path, FileAttributes attributes);
    string? GetOwnerDisplayName(string path);
    void ApplyAttributes(string path, FileAttributes currentAttributes, IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes);
    void OpenSystemProperties(string path);
    bool CanOpenSystemProperties { get; }
}
