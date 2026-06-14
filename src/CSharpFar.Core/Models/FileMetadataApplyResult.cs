namespace CSharpFar.Core.Models;

public sealed record FileMetadataApplyResult(
    int ProcessedCount,
    int ChangedCount,
    IReadOnlyList<FileMetadataApplyError> Errors);

public sealed record FileMetadataApplyError(
    string Path,
    string Operation,
    string Message,
    Exception? Exception = null);
