using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationPlanBuilder
{
    FileOperationPlan BuildPlan(FileOperationRequest request, CancellationToken cancellationToken = default);
}
