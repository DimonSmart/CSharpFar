using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationConflictResolver
{
    FileOperationConflictDecision Resolve(FileOperationConflict conflict);
}
