using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileMetadataService
{
    FileMetadataSnapshot GetMetadata(string path);
    FileMetadataSnapshot GetMergedMetadata(IReadOnlyList<string> paths);
    FileMetadataApplyResult ApplyMetadata(
        IReadOnlyList<string> paths,
        FileMetadataChangeSet changes);
}
