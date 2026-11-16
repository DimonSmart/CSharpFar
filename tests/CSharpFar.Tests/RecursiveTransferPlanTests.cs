using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class RecursiveTransferPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "csharpfar-transfer-plan-" + Guid.NewGuid().ToString("N"));

    public RecursiveTransferPlanTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ProviderCopy_PreviewsNestedFilesAndReportsExactKnownTotals()
    {
        var source = BuildTree();
        var registry = new FilePanelSourceRegistry([source]);
        var service = new FileOperationService(registry);
        FileOperationRequest request = CopyRequest(source, "/src", "/dest");
        FileOperationPlan preview = service.BuildPlan(request);
        Assert.Equal(
            new[] {
                "/dest/src",
                "/dest/src/nested",
                "/dest/src/nested/alpha.txt",
                "/dest/src/nested/empty",
                "/dest/src/beta.txt",
            },
            preview.Items.Select(item => item.Destination.SourcePath).ToArray());
        Assert.Equal(2, preview.Items.Count(item => !item.IsDirectory));
        var progress = new InlineProgress();

        FileOperationResult result = await service.ExecuteAsync(
            request, progress, new NoConflicts());

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.CopiedCount);
        Assert.Equal(5, result.TotalBytes);
        Assert.Equal(5, result.BytesProcessed);
        Assert.Equal("abc", source.ReadText("/dest/src/nested/alpha.txt"));
        Assert.Equal("de", source.ReadText("/dest/src/beta.txt"));
        Assert.NotNull(source.GetItem("/dest/src/nested/empty"));
        Assert.Contains(progress.Frames, frame =>
            frame.Phase == FileOperationPhase.Scanning && frame.TotalBytesDone == 5);
        Assert.Contains(progress.Frames, frame =>
            frame.Phase == FileOperationPhase.Copying &&
            frame.TotalBytesTotal == 5 && frame.ItemsTotal == 5);
        Assert.Equal(5, source.LastPlan!.Items.Count);
    }

    [Fact]
    public async Task ProviderCopy_ExecutesExactlyOneSourceEnumerationPerDirectory()
    {
        var source = BuildTree();
        var service = new FileOperationService(new FilePanelSourceRegistry([source]));
        await service.ExecuteAsync(
            CopyRequest(source, "/src", "/dest"), null, new NoConflicts());

        Assert.Equal(3, source.EnumerationCount); // /src, /src/nested, /src/nested/empty
        Assert.Equal("abc", source.ReadText("/dest/src/nested/alpha.txt"));
    }

    [Fact]
    public async Task ProviderCopy_NestedScanFailureNeverBeginsMutationBatch()
    {
        var source = BuildTree();
        source.FailEnumeratePath = "/src/nested";
        var service = new FileOperationService(new FilePanelSourceRegistry([source]));

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(
            CopyRequest(source, "/src", "/dest"), null, new NoConflicts()));

        Assert.Equal(0, source.Begun);
        Assert.Null(source.GetItem("/dest/src"));
        Assert.Equal("abc", source.ReadText("/src/nested/alpha.txt"));
    }

    [Fact]
    public async Task CrossProviderMove_UsesPreflightTotalsAndKeepsCopyDeleteOrdering()
    {
        string sourceDirectory = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "nested", "file.txt"), "xyz");

        var target = new InMemoryBatchFilePanelSource();
        target.SeedDirectory("/dest");
        var registry = new FilePanelSourceRegistry([
            new LocalFilePanelSource(new FileSystemService()), target,
        ]);
        var service = new FileOperationService(registry);
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [],
            SourceLocations = [PanelLocation.Local(sourceDirectory)],
            DestinationLocation = new PanelLocation(target.SourceId, "/dest"),
            Options = new FileOperationOptions(),
        };
        var progress = new InlineProgress();

        FileOperationResult result = await service.ExecuteAsync(request, progress, new NoConflicts());

        Assert.Empty(result.Errors);
        Assert.False(result.PartiallyMoved);
        Assert.Equal(3, result.TotalBytes);
        Assert.Equal(3, result.BytesProcessed);
        Assert.Equal("xyz", target.ReadText("/dest/source/nested/file.txt"));
        Assert.False(Directory.Exists(sourceDirectory));
        Assert.Equal(1, target.Committed);
        Assert.Contains(progress.Frames, p => p.TotalBytesTotal == 3 && p.ItemsTotal == 3);
    }

    private static InMemoryBatchFilePanelSource BuildTree()
    {
        var provider = new InMemoryBatchFilePanelSource();
        provider.SeedDirectory("/src");
        provider.SeedDirectory("/src/nested");
        provider.SeedDirectory("/src/nested/empty");
        provider.SeedDirectory("/dest");
        provider.SeedFile("/src/nested/alpha.txt", "abc");
        provider.SeedFile("/src/beta.txt", "de");
        return provider;
    }

    private static FileOperationRequest CopyRequest(
        InMemoryBatchFilePanelSource provider, string source, string destination) => new()
    {
        Kind = FileOperationKind.Copy,
        Sources = [],
        SourceLocations = [new PanelLocation(provider.SourceId, source)],
        DestinationLocation = new PanelLocation(provider.SourceId, destination),
        Options = new FileOperationOptions(),
    };

    private sealed class InlineProgress : IProgress<FileOperationProgress>
    {
        public List<FileOperationProgress> Frames { get; } = [];
        public void Report(FileOperationProgress value) => Frames.Add(value);
    }

    private sealed class NoConflicts : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            throw new InvalidOperationException("Unexpected conflict: " + conflict.DestinationPath);
    }
}
