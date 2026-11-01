using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class ArchiveBatchMutationTests
{
    private sealed class Resolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private static Task<FileOperationResult> Execute(
        InMemoryBatchFilePanelSource source,
        FileOperationKind kind,
        IReadOnlyList<string> paths,
        string? destination = null)
    {
        var registry = new FilePanelSourceRegistry([source]);
        return new FileOperationService(registry).ExecuteAsync(new FileOperationRequest
        {
            Kind = kind,
            Sources = [],
            SourceLocations = paths.Select(path => new PanelLocation(source.SourceId, path)).ToArray(),
            DestinationLocation = destination is null ? null : new PanelLocation(source.SourceId, destination),
            Options = new FileOperationOptions(),
        }, null, new Resolver());
    }

    [Fact]
    public async Task DeleteManyFiles_UsesOneCommit()
    {
        var source = new InMemoryBatchFilePanelSource();
        for (int i = 0; i < 500; i++)
            source.SeedFile($"/file-{i}.txt", "data");

        var result = await Execute(source, FileOperationKind.Delete,
            Enumerable.Range(0, 500).Select(i => $"/file-{i}.txt").ToArray());

        Assert.Empty(result.Errors);
        Assert.Equal(500, result.DeletedCount);
        Assert.Empty(source.EnumerateDirectory("/"));
        Assert.Equal(1, source.Begun);
        Assert.Equal(1, source.Committed);
    }

    [Fact]
    public async Task DeleteCommitFailure_PreservesAllEntries()
    {
        var source = new InMemoryBatchFilePanelSource { FailCommit = true };
        source.SeedFile("/first", "one");
        source.SeedFile("/second", "two");

        await Assert.ThrowsAsync<IOException>(() =>
            Execute(source, FileOperationKind.Delete, ["/first", "/second"]));

        Assert.Equal("one", source.ReadText("/first"));
        Assert.Equal("two", source.ReadText("/second"));
        Assert.Equal(0, source.Committed);
        Assert.Equal(1, source.Aborted);
    }

    [Fact]
    public async Task MoveSeveralFiles_RewritesOnce()
    {
        var source = new InMemoryBatchFilePanelSource();
        source.SeedDirectory("/to");
        source.SeedFile("/a.txt", "a");
        source.SeedFile("/b.txt", "b");

        var result = await Execute(source, FileOperationKind.Move,
            ["/a.txt", "/b.txt"], "/to");

        Assert.Empty(result.Errors);
        Assert.Equal("a", source.ReadText("/to/a.txt"));
        Assert.Equal("b", source.ReadText("/to/b.txt"));
        Assert.Null(source.ReadText("/a.txt"));
        Assert.Equal(1, source.Begun);
        Assert.Equal(1, source.Committed);
    }

    [Fact]
    public async Task MoveCommitFailure_DoesNotRemoveSource()
    {
        var source = new InMemoryBatchFilePanelSource { FailCommit = true };
        source.SeedDirectory("/to");
        source.SeedFile("/a.txt", "a");

        await Assert.ThrowsAsync<IOException>(() =>
            Execute(source, FileOperationKind.Move, ["/a.txt"], "/to"));

        Assert.Equal("a", source.ReadText("/a.txt"));
        Assert.Null(source.ReadText("/to/a.txt"));
        Assert.Equal(1, source.Aborted);
    }

    [Fact]
    public async Task CreateDirectory_UsesBatch()
    {
        var source = new InMemoryBatchFilePanelSource();
        var result = await Execute(source, FileOperationKind.CreateDirectory, [], "/created");

        Assert.Empty(result.Errors);
        Assert.True(source.GetItem("/created")!.IsDirectory);
        Assert.Equal(1, source.Begun);
        Assert.Equal(1, source.Committed);
    }

    [Fact]
    public async Task CreateDirectoryCommitFailure_LeavesNoDirectory()
    {
        var source = new InMemoryBatchFilePanelSource { FailCommit = true };
        await Assert.ThrowsAsync<IOException>(() =>
            Execute(source, FileOperationKind.CreateDirectory, [], "/created"));
        Assert.Null(source.GetItem("/created"));
        Assert.Equal(1, source.Aborted);
    }
}
