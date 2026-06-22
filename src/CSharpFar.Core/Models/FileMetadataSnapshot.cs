namespace CSharpFar.Core.Models;

public sealed record FileMetadataSnapshot(
    string Path,
    string DisplayName,
    bool IsDirectory,
    FileAttributes Attributes,
    DateTime? CreationTime,
    DateTime? LastWriteTime,
    DateTime? LastAccessTime,
    string? OwnerDisplayName,
    IReadOnlyList<FileAttributeDescriptor> AttributesDescriptors,
    IReadOnlyDictionary<FileAttributeId, AttributeEditState> AttributeStates,
    bool CanEditCreationTime,
    bool CanEditLastWriteTime,
    bool CanEditLastAccessTime,
    UnixFileMetadata? UnixMetadata);
