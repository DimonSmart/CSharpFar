using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileSystemPlatformOperations
{
    bool SupportsRecycleBin { get; }

    void DeleteFile(string path, bool useRecycleBin);

    void DeleteDirectory(string path, bool recursive, bool useRecycleBin);

    bool IsSymbolicLink(string path);

    bool TryCopySymbolicLink(string sourcePath, string destinationPath, out string? error);

    void PreserveFileMetadata(
        string sourcePath,
        string destinationPath,
        FileOperationOptions options,
        IFileOperationErrorSink errors);
}
