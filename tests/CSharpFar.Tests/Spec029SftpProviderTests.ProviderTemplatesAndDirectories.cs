using System.Text;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

using static CSharpFar.Tests.RemoteConnectionDialogTestFixture;

namespace CSharpFar.Tests;

public sealed partial class Spec029SftpProviderTests
{
    [Fact]
    public async Task ProviderCopy_TemplateCreatesMissingNestedDirectoriesForFile()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/camera/IMG001.jpg", "photo");

        await service.ExecuteAsync(
            ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/camera/IMG001.jpg"], "/Photos/{modified:yyyy}/{name}{ext}", new FileOperationOptions(), useDestinationTemplate: true),
            null,
            new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Photos/1970/IMG001.jpg"));
    }

    [Fact]
    public async Task ProviderCopy_TemplateCreatesMissingNestedDirectoriesForDirectoryAndPreservesDescendants()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/camera/Vacation/IMG001.jpg", "photo");

        await service.ExecuteAsync(
            ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/camera/Vacation"], "/Photos/{modified:yyyy}/{name}", new FileOperationOptions(), useDestinationTemplate: true),
            null,
            new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Photos/1970/Vacation/IMG001.jpg"));
        Assert.Null(remote.GetItem("/Photos/1970/Vacation/1970/IMG001.jpg"));
    }

    [Fact]
    public async Task ProviderCopy_TemplateSupportsMixedFileAndDirectorySelection()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/camera/cover.jpg", "cover");
        remote.WriteFile("/camera/Vacation/IMG001.jpg", "photo");

        await service.ExecuteAsync(
            ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/camera/cover.jpg", "/camera/Vacation"], "/Photos/{modified:yyyy}/{name}{ext}", new FileOperationOptions(), useDestinationTemplate: true),
            null,
            new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Photos/1970/cover.jpg"));
        Assert.NotNull(remote.GetItem("/Photos/1970/Vacation/IMG001.jpg"));
    }

    [Fact]
    public async Task ProviderCopy_TemplateRejectsSftpBackslashNameForWindowsDestinationBeforeMutation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(_tempDir);
        var remote = new MemoryPanelSource(new PanelSourceId("fake-remote"));
        remote.WriteFile("/camera/foo\\bar.jpg", "photo");
        var registry = new FilePanelSourceRegistry(
        [
            remote,
            new LocalFilePanelSource(new FileSystemService()),
        ]);
        var service = new FileOperationService(registry);

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [new PanelLocation(remote.SourceId, "/camera/foo\\bar.jpg")],
                Destination = Path.Combine(_tempDir, "{name}{ext}"),
                DestinationLocation = PanelLocation.Local(Path.Combine(_tempDir, "{name}{ext}")),
                UseDestinationTemplate = true,
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new NoOpConflictResolver()));

        Assert.NotNull(remote.GetItem("/camera/foo\\bar.jpg"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tempDir));
    }

    [Fact]
    public async Task ProviderCopy_TemplateAllowsBackslashNameForSftpDestination()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/camera/foo\\bar.jpg", "photo");

        await service.ExecuteAsync(
            ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/camera/foo\\bar.jpg"], "/Photos/{name}{ext}", new FileOperationOptions(), useDestinationTemplate: true),
            null,
            new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Photos/foo\\bar.jpg"));
    }

    [Fact]
    public async Task ProviderMove_DirectoryMovesOnlyMatchingFiles()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/Source/sub/A.txt", "text");
        remote.WriteFile("/Source/sub/B.csv", "csv");
        await remote.CreateDirectoryAsync("/Target");

        await service.ExecuteAsync(ProviderRequest(FileOperationKind.Move, remote.SourceId, ["/Source"], "/Target/*_old", new FileOperationOptions { FileMask = "*.txt" }), null, new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Target/Source_old/sub/A.txt"));
        Assert.Null(remote.GetItem("/Source/sub/A.txt"));
        Assert.NotNull(remote.GetItem("/Source/sub/B.csv"));
    }

    [Fact]
    public async Task ProviderCopy_DirectoryCopiesOnlyMatchingFiles()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/Source/A.txt", "text");
        remote.WriteFile("/Source/B.csv", "csv");
        await remote.CreateDirectoryAsync("/Target");

        await service.ExecuteAsync(ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/Source"], "/Target/*_old", new FileOperationOptions { FileMask = "*.txt" }), null, new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Target/Source_old/A.txt"));
        Assert.Null(remote.GetItem("/Target/Source_old/B.csv"));
        Assert.NotNull(remote.GetItem("/Source/A.txt"));
    }

    [Fact]
    public async Task ProviderCopy_MaskedLocalDirectoryUsesProviderTargetPathSyntax()
    {
        string sourcePath = Path.Combine(_tempDir, "Source");
        string nestedPath = Path.Combine(sourcePath, "sub");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(Path.Combine(nestedPath, "A.txt"), "text");
        var service = CreateProviderOperationService(out var remote);

        await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Local(sourcePath)],
                Destination = "/Moved/*_old",
                DestinationLocation = new PanelLocation(remote.SourceId, "/Moved/*_old"),
                Options = new FileOperationOptions { FileMask = "*.txt" },
            },
            null,
            new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Moved/Source_old/sub/A.txt"));
    }

    [Fact]
    public async Task ProviderCopy_MaskedProviderDirectoryUsesLocalTargetPathSyntax()
    {
        Directory.CreateDirectory(_tempDir);
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/Source/sub/A.txt", "text");
        string destinationPath = Path.Combine(_tempDir, "Moved", "*_old");

        await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [new PanelLocation(remote.SourceId, "/Source")],
                Destination = destinationPath,
                DestinationLocation = PanelLocation.Local(destinationPath),
                Options = new FileOperationOptions { FileMask = "*.txt" },
            },
            null,
            new NoOpConflictResolver());

        Assert.True(File.Exists(Path.Combine(_tempDir, "Moved", "Source_old", "sub", "A.txt")));
    }

    [Fact]
    public async Task ProviderMove_SkippedMatchingFileRemainsAtSource()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/Source/A.txt", "a");
        remote.WriteFile("/Source/B.txt", "b");
        remote.WriteFile("/Target/Source/B.txt", "existing");

        await service.ExecuteAsync(ProviderRequest(FileOperationKind.Move, remote.SourceId, ["/Source"], "/Target", new FileOperationOptions { FileMask = "*.txt" }), null, new FixedConflictResolver(ConflictDecisionMode.Skip));

        Assert.Null(remote.GetItem("/Source/A.txt"));
        Assert.NotNull(remote.GetItem("/Target/Source/A.txt"));
        Assert.NotNull(remote.GetItem("/Source/B.txt"));
        Assert.NotNull(remote.GetItem("/Target/Source/B.txt"));
    }

    [Fact]
    public async Task ProviderMove_NoMatchingFilesDoesNotMutateProvider()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/Source/A.csv", "csv");

        FileOperationResult result = await service.ExecuteAsync(ProviderRequest(FileOperationKind.Move, remote.SourceId, ["/Source"], "/Target/*_old", new FileOperationOptions { FileMask = "*.txt" }), null, new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/Source/A.csv"));
        Assert.Null(remote.GetItem("/Target"));
        Assert.Equal(0, result.MovedCount);
    }

    [Fact]
    public void PanelLocation_SelectionKeyIncludesSourceId()
    {
        var left = new PanelLocation(new PanelSourceId("left"), "/same/path.txt");
        var right = new PanelLocation(new PanelSourceId("right"), "/same/path.txt");

        Assert.NotEqual(left.SelectionKey, right.SelectionKey);
    }

}
