using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class LocalCopySharedPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "csharpfar-local-shared-" + Guid.NewGuid().ToString("N"));

    public LocalCopySharedPipelineTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task DefaultLocalCopy_UsesRecursivePlanAndPreservesFileAndDirectoryTimestamps()
    {
        string source = Path.Combine(_root, "src");
        string folder = Path.Combine(source, "nested");
        string destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(destination);
        string file = Path.Combine(folder, "text.txt");
        await File.WriteAllTextAsync(file, "contents");
        DateTime oldTime = new(2024, 1, 2, 12, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, oldTime);
        Directory.SetLastWriteTimeUtc(folder, oldTime);
        Directory.SetLastWriteTimeUtc(source, oldTime);
        var registry = LocalRegistry();
        var progress = new InlineProgress();
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [source],
            Destination = destination,
            Options = new FileOperationOptions(),
        };
        var service = new FileOperationService(registry);
        FileOperationPlan preview = service.BuildPlan(request);
        FileOperationResult result = await service.ExecuteAsync(
            request, progress, new Resolver());

        string copyRoot = Path.Combine(destination, "src");
        string copiedFolder = Path.Combine(copyRoot, "nested");
        string copiedFile = Path.Combine(copiedFolder, "text.txt");
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(8, result.TotalBytes);
        Assert.Equal("contents", await File.ReadAllTextAsync(copiedFile));
        Assert.Equal(
            preview.Items.Select(item => item.Destination.SourcePath).Order().ToArray(),
            new[] { copyRoot, copiedFolder, copiedFile }.Order().ToArray());
        Assert.InRange(Math.Abs((File.GetLastWriteTimeUtc(copiedFile) - oldTime).TotalSeconds), 0, 2);
        Assert.InRange(Math.Abs((Directory.GetLastWriteTimeUtc(copiedFolder) - oldTime).TotalSeconds), 0, 2);
        Assert.Contains(progress.Frames, p =>
            p.Phase == FileOperationPhase.Copying && p.TotalBytesTotal == 8 && p.ItemsTotal == 3);
    }

    [Fact]
    public async Task DefaultLocalCopy_RepeatedRenameConflictNeverOverwritesProtectedFile()
    {
        string source = Path.Combine(_root, "source.txt");
        string destination = Path.Combine(_root, "target.txt");
        string protectedFile = Path.Combine(_root, "protected.txt");
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(destination, "old");
        await File.WriteAllTextAsync(protectedFile, "protected");
        var resolver = new Resolver(
            new FileOperationConflictDecision
            {
                Mode = ConflictDecisionMode.Rename,
                NewDestinationPath = protectedFile,
            },
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip));
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [source],
            Destination = destination,
            Options = new FileOperationOptions(),
        };
        FileOperationResult result = await new FileOperationService(LocalRegistry())
            .ExecuteAsync(request, null, resolver);

        Assert.Equal(2, resolver.CallCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("old", await File.ReadAllTextAsync(destination));
        Assert.Equal("protected", await File.ReadAllTextAsync(protectedFile));
        Assert.Equal("new", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task DefaultLocalCopy_RefusesSelfCopyWithoutModifyingSource()
    {
        string path = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(path, "keep");
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [path],
            Destination = path,
            Options = new FileOperationOptions(),
        };

        await Assert.ThrowsAsync<IOException>(() =>
            new FileOperationService(LocalRegistry()).ExecuteAsync(request, null, new Resolver()));
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DefaultLocalCopy_LocalLocationsRequestCopiesMultipleFiles()
    {
        string src = Path.Combine(_root, "source");
        string dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        string first = Path.Combine(src, "first.txt");
        string second = Path.Combine(src, "second.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [first, second],
            SourceLocations = [PanelLocation.Local(first), PanelLocation.Local(second)],
            Destination = dest,
            DestinationLocation = PanelLocation.Local(dest),
            Options = new FileOperationOptions(),
        };

        FileOperationResult result = await new FileOperationService(LocalRegistry())
            .ExecuteAsync(request, null, new Resolver());

        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(dest, "first.txt")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(dest, "second.txt")));
    }

    private static FilePanelSourceRegistry LocalRegistry() =>
        new([new LocalFilePanelSource(new FileSystemService())]);

    private sealed class InlineProgress : IProgress<FileOperationProgress>
    {
        public List<FileOperationProgress> Frames { get; } = [];
        public void Report(FileOperationProgress value) => Frames.Add(value);
    }

    private sealed class Resolver(params FileOperationConflictDecision[] decisions)
        : IFileOperationConflictResolver
    {
        public int CallCount { get; private set; }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            if (CallCount >= decisions.Length)
                throw new InvalidOperationException("Unexpected conflict: " + conflict.DestinationPath);
            return decisions[CallCount++];
        }
    }
}
