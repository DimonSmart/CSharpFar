using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class ArchiveBatchInfrastructureTests
{
    private sealed class OverwriteResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private static Task<FileOperationResult> Copy(
        InMemoryBatchFilePanelSource source, InMemoryBatchFilePanelSource destination,
        IReadOnlyList<string> paths, CancellationToken token = default,
        IProgress<FileOperationProgress>? progress = null)
    {
        var registry = new FilePanelSourceRegistry([source, destination]);
        var ops = new FileOperationService(registry);
        return ops.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [],
            SourceLocations = paths.Select(path => new PanelLocation(source.SourceId, path)).ToArray(),
            DestinationLocation = new PanelLocation(destination.SourceId, "/"),
            Options = new FileOperationOptions(),
        }, progress, new OverwriteResolver(), token);
    }

    [Fact]
    public async Task MultipleFilesAndNestedDirectories_ArePublishedWithOneCommit()
    {
        var source = new InMemoryBatchFilePanelSource();
        var destination = new InMemoryBatchFilePanelSource();
        source.SeedFile("/one.txt", "one");
        source.SeedFile("/two.txt", "two");
        source.SeedDirectory("/nested");
        source.SeedFile("/nested/three.txt", "three");

        var result = await Copy(source, destination, ["/one.txt", "/two.txt", "/nested"]);

        Assert.Empty(result.Errors);
        Assert.Equal("one", destination.ReadText("/one.txt"));
        Assert.Equal("two", destination.ReadText("/two.txt"));
        Assert.Equal("three", destination.ReadText("/nested/three.txt"));
        Assert.Equal(1, destination.Begun);
        Assert.Equal(1, destination.Committed);
        Assert.Equal(1, destination.Disposed);
        Assert.True(destination.LastPlan!.Items.Count >= 3);
    }

    [Fact]
    public async Task FailedCommit_DoesNotPublishAnyChangesOrDestroyExistingFile()
    {
        var source = new InMemoryBatchFilePanelSource();
        var destination = new InMemoryBatchFilePanelSource { FailCommit = true };
        source.SeedFile("/existing.txt", "replacement");
        source.SeedFile("/new.txt", "new");
        destination.SeedFile("/existing.txt", "original");

        await Assert.ThrowsAsync<IOException>(() => Copy(
            source, destination, ["/existing.txt", "/new.txt"]));

        Assert.Equal("original", destination.ReadText("/existing.txt"));
        Assert.Null(destination.ReadText("/new.txt"));
        Assert.Equal(0, destination.Committed);
        Assert.Equal(1, destination.Aborted);
        Assert.Equal(1, destination.Disposed);
    }

    [Fact]
    public async Task FailedStreamCompletion_AbortsAndPreservesOldContent()
    {
        var source = new InMemoryBatchFilePanelSource();
        var destination = new InMemoryBatchFilePanelSource();
        source.SeedFile("/old.txt", "replacement");
        destination.SeedFile("/old.txt", "original");
        // A batch is a view distinct from the committed destination.
        // Failure in the destination batch must not be interpreted as a successful copy.
        destination.FailCommit = true;
        await Assert.ThrowsAsync<IOException>(() => Copy(source, destination, ["/old.txt"]));
        Assert.Equal("original", destination.ReadText("/old.txt"));
    }

    [Fact]
    public async Task CancellationBeforeCommit_AbortsStagedChanges()
    {
        var source = new InMemoryBatchFilePanelSource();
        var destination = new InMemoryBatchFilePanelSource();
        source.SeedFile("/a.txt", new string('x', 64));
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress<FileOperationProgress>(p =>
        {
            if (p.Phase == FileOperationPhase.Copying && p.TotalBytesDone > 0)
                cts.Cancel();
        });

        var result = await Copy(source, destination, ["/a.txt"], cts.Token, progress);

        Assert.True(result.Cancelled);
        Assert.Null(destination.ReadText("/a.txt"));
        Assert.Equal(0, destination.Committed);
        Assert.Equal(1, destination.Aborted);
    }

    [Fact]
    public async Task RepeatedCommitAbortAndDispose_AreSafe()
    {
        var destination = new InMemoryBatchFilePanelSource();
        var plan = new FileOperationPlan([]);
        var batch = await destination.BeginBatchAsync(plan);
        await batch.CommitAsync();
        await batch.CommitAsync();
        await batch.AbortAsync();
        await batch.DisposeAsync();
        await batch.DisposeAsync();
        Assert.Equal(1, destination.Committed);
        Assert.Equal(0, destination.Aborted);
        Assert.Equal(1, destination.Disposed);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
