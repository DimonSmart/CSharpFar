using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationPlanBuilder
{
    FileOperationPlan BuildPlan(FileOperationRequest request, CancellationToken cancellationToken = default);

    Task<FileOperationPlan> BuildPlanAsync(FileOperationRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildPlan(request, cancellationToken), cancellationToken);
}
