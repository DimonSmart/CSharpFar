using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Platform.MacOs;

public sealed class MacOsFileSystemPlatformOperations : IFileSystemPlatformOperations
{
    public bool SupportsRecycleBin => false;
    public void DeleteFile(string path, bool useRecycleBin) { ThrowIfRecycleBinRequested(useRecycleBin); File.Delete(path); }
    public void DeleteDirectory(string path, bool recursive, bool useRecycleBin) { ThrowIfRecycleBinRequested(useRecycleBin); Directory.Delete(path, recursive); }
    public bool IsSymbolicLink(string path) => (File.Exists(path) && new FileInfo(path).LinkTarget is not null) || (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null);
    public bool TryCopySymbolicLink(string sourcePath, string destinationPath, out string? error)
    {
        error = null; string? target = Directory.Exists(sourcePath) ? new DirectoryInfo(sourcePath).LinkTarget : new FileInfo(sourcePath).LinkTarget;
        if (string.IsNullOrWhiteSpace(target)) { error = "Cannot copy symbolic link because its target is unavailable."; return false; }
        if (Directory.Exists(sourcePath)) Directory.CreateSymbolicLink(destinationPath, target); else File.CreateSymbolicLink(destinationPath, target); return true;
    }
    public void PreserveFileMetadata(string sourcePath, string destinationPath, FileOperationOptions options, IFileOperationErrorSink errors)
    {
        try
        {
            if (options.PreserveTimestamps) { File.SetCreationTime(destinationPath, File.GetCreationTime(sourcePath)); File.SetLastWriteTime(destinationPath, File.GetLastWriteTime(sourcePath)); File.SetLastAccessTime(destinationPath, File.GetLastAccessTime(sourcePath)); }
            if (options.PreserveAttributes) File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { errors.AddError(destinationPath, ex.Message); }
    }
    private static void ThrowIfRecycleBinRequested(bool useRecycleBin) { if (useRecycleBin) throw new PlatformNotSupportedException("Recycle bin is not supported on macOS yet."); }
}
