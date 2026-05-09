using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationService
{
    Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        IFileOperationConflictResolver conflictResolver,
        CancellationToken cancellationToken = default);
}
