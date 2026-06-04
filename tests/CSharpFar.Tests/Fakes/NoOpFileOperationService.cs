using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests.Fakes;

public sealed class NoOpFileOperationService : IFileOperationService
{
    public Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        IFileOperationConflictResolver conflictResolver,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
}
