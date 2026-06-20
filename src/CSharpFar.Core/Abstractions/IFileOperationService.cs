using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationService
{
    bool SupportsRecycleBin { get; }

    Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        IFileOperationConflictResolver conflictResolver,
        CancellationToken cancellationToken = default);
}
