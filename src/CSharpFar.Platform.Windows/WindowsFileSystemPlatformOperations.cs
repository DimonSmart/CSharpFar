using System.Reflection;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace CSharpFar.Platform.Windows;

public sealed class WindowsFileSystemPlatformOperations : IFileSystemPlatformOperations
{
    public bool SupportsRecycleBin => true;

    public void DeleteFile(string path, bool useRecycleBin)
    {
        if (useRecycleBin)
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive, bool useRecycleBin)
    {
        if (useRecycleBin)
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            Directory.Delete(path, recursive);
    }

    public bool IsSymbolicLink(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public bool TryCopySymbolicLink(string sourcePath, string destinationPath, out string? error)
    {
        error = null;
        string? target = Directory.Exists(sourcePath)
            ? new DirectoryInfo(sourcePath).LinkTarget
            : new FileInfo(sourcePath).LinkTarget;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Cannot copy link because its target is unavailable.";
            return false;
        }

        if (Directory.Exists(sourcePath))
            Directory.CreateSymbolicLink(destinationPath, target);
        else
            File.CreateSymbolicLink(destinationPath, target);
        return true;
    }

    public void PreserveFileMetadata(
        string sourcePath,
        string destinationPath,
        FileOperationOptions options,
        IFileOperationErrorSink errors)
    {
        TryPreserveTimes(sourcePath, destinationPath, options, errors);
        if (options.PreserveAttributes)
            TrySetAttributes(sourcePath, destinationPath, errors);
        if (options.SecurityMode == FileSecurityMode.CopyAccessControl)
            TryCopyAccessControl(sourcePath, destinationPath, errors);
    }

    private static void TryPreserveTimes(
        string sourcePath,
        string destinationPath,
        FileOperationOptions options,
        IFileOperationErrorSink errors)
    {
        if (!options.PreserveTimestamps)
            return;

        try
        {
            if (Directory.Exists(sourcePath))
            {
                Directory.SetCreationTime(destinationPath, Directory.GetCreationTime(sourcePath));
                Directory.SetLastWriteTime(destinationPath, Directory.GetLastWriteTime(sourcePath));
                Directory.SetLastAccessTime(destinationPath, Directory.GetLastAccessTime(sourcePath));
                return;
            }

            File.SetCreationTime(destinationPath, File.GetCreationTime(sourcePath));
            File.SetLastWriteTime(destinationPath, File.GetLastWriteTime(sourcePath));
            File.SetLastAccessTime(destinationPath, File.GetLastAccessTime(sourcePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.AddError(destinationPath, ex.Message);
        }
    }

    private static void TrySetAttributes(string sourcePath, string destinationPath, IFileOperationErrorSink errors)
    {
        try
        {
            File.SetAttributes(destinationPath, File.GetAttributes(sourcePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.AddError(destinationPath, ex.Message);
        }
    }

    private static void TryCopyAccessControl(string sourcePath, string destinationPath, IFileOperationErrorSink errors)
    {
        try
        {
            Type infoType = Directory.Exists(sourcePath) ? typeof(DirectoryInfo) : typeof(FileInfo);
            object sourceInfo = Directory.Exists(sourcePath) ? new DirectoryInfo(sourcePath) : new FileInfo(sourcePath);
            object destinationInfo = Directory.Exists(destinationPath) ? new DirectoryInfo(destinationPath) : new FileInfo(destinationPath);
            MethodInfo? getAccessControl = infoType.GetMethod("GetAccessControl", Type.EmptyTypes);
            MethodInfo? setAccessControl = infoType.GetMethods()
                .FirstOrDefault(m => m.Name == "SetAccessControl" && m.GetParameters().Length == 1);

            if (getAccessControl is null || setAccessControl is null)
                throw new PlatformNotSupportedException("Access control copy is not available in this runtime.");

            object? accessControl = getAccessControl.Invoke(sourceInfo, null);
            setAccessControl.Invoke(destinationInfo, [accessControl]);
        }
        catch (Exception ex) when (ex is TargetInvocationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            errors.AddError(destinationPath, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
