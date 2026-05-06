using CSharpFar.Core.Abstractions;

namespace CSharpFar.FileSystem;

public sealed class FileOperationService : IFileOperationService
{
    public void CreateDirectory(string path)
    {
        if (Directory.Exists(path))
            throw new IOException($"Folder '{Path.GetFileName(path)}' already exists.");
        Directory.CreateDirectory(path);
    }

    public Task CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Coming in Stage 7.");

    public Task MoveAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Coming in Stage 8.");

    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Coming in Stage 9.");
}
