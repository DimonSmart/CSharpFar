namespace CSharpFar.Core.Abstractions;

public interface IFileOperationService
{
    Task CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default);
    Task MoveAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
    void CreateDirectory(string path);
}
