using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Platform.Unix;
using CSharpFar.Platform.Windows;

namespace CSharpFar.Tests;

public sealed class FileSystemPlatformOperationsTests
{
    [Fact]
    public void PlatformOperations_ReportRecycleBinSupport()
    {
        Assert.True(new WindowsFileSystemPlatformOperations().SupportsRecycleBin);
        Assert.False(new UnixFileSystemPlatformOperations().SupportsRecycleBin);
    }

    [Fact]
    public void Windows_IsSymbolicLink_ReturnsFalseForOrdinaryFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = Path.GetTempFileName();
        try
        {
            Assert.False(new WindowsFileSystemPlatformOperations().IsSymbolicLink(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Windows_DeleteFile_RemovesPermanentFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = Path.GetTempFileName();
        new WindowsFileSystemPlatformOperations().DeleteFile(path, useRecycleBin: false);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Unix_RecycleBinDelete_IsExplicitlyUnsupported()
    {
        var operations = new UnixFileSystemPlatformOperations();

        Assert.Throws<PlatformNotSupportedException>(() => operations.DeleteFile("/tmp/not-created", useRecycleBin: true));
    }

    [Fact]
    public void Unix_PermanentDelete_RemovesFileAndDirectoryRecursively()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        string file = Path.Combine(directory, "file.txt");
        string childDirectory = Path.Combine(directory, "child");
        File.WriteAllText(file, "content");
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(childDirectory, "child.txt"), "content");
        var operations = new UnixFileSystemPlatformOperations();

        operations.DeleteFile(file, useRecycleBin: false);
        operations.DeleteDirectory(childDirectory, recursive: true, useRecycleBin: false);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(childDirectory));
        Directory.Delete(directory);
    }

    [Fact]
    public void Unix_PreserveFileMetadata_CopiesUnixMode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Directory.CreateTempSubdirectory().FullName;
        string source = Path.Combine(directory, "source");
        string destination = Path.Combine(directory, "destination");
        File.WriteAllText(source, "source");
        File.WriteAllText(destination, "destination");
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        new UnixFileSystemPlatformOperations().PreserveFileMetadata(
            source,
            destination,
            new FileOperationOptions { PreserveAttributes = true, PreserveTimestamps = false },
            new RecordingErrorSink());

        Assert.Equal(File.GetUnixFileMode(source), File.GetUnixFileMode(destination));
    }

    [Fact]
    public void Unix_TryCopySymbolicLink_PreservesRelativeTarget()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Directory.CreateTempSubdirectory().FullName;
        string target = Path.Combine(directory, "target");
        string sourceLink = Path.Combine(directory, "source-link");
        string destinationLink = Path.Combine(directory, "destination-link");
        File.WriteAllText(target, "target");
        File.CreateSymbolicLink(sourceLink, "target");

        bool copied = new UnixFileSystemPlatformOperations().TryCopySymbolicLink(sourceLink, destinationLink, out string? error);

        Assert.True(copied, error);
        Assert.Equal("target", new FileInfo(destinationLink).LinkTarget);
    }

    private sealed class RecordingErrorSink : IFileOperationErrorSink
    {
        public void AddError(string path, string message)
        {
            throw new InvalidOperationException($"{path}: {message}");
        }
    }
}
