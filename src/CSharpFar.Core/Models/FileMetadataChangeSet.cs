namespace CSharpFar.Core.Models;

public sealed record FileMetadataChangeSet(
    IReadOnlyDictionary<FileAttributeId, AttributeEditState> AttributeChanges,
    DateTime? CreationTime,
    DateTime? LastWriteTime,
    DateTime? LastAccessTime);
