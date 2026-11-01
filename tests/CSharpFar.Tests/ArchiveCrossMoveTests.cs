using System.IO.Compression;
using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;

namespace CSharpFar.Tests;

public sealed class ArchiveCrossMoveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "csharpfar-cross-" + Guid.NewGuid().ToString("N"));
    public ArchiveCrossMoveTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static LocalFilePanelSource Local() => new(new FileSystemService());

    private string Zip(string name, params (string Name, string Text)[] contents)
    {
        string file = Path.Combine(_folder, name);
        using var zip = new ZipArchive(File.Create(file), ZipArchiveMode.Create);
        foreach (var (entryName, value) in contents)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open(), Encoding.UTF8);
            writer.Write(value);
        }
        return file;
    }

    private sealed class Resolver(ConflictDecisionMode decision) : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict _) =>
            FileOperationConflictDecision.FromMode(decision);
    }

    private static Task<FileOperationResult> Move(
        FilePanelSourceRegistry registry, PanelLocation source,
        PanelLocation destination, ConflictDecisionMode conflict = ConflictDecisionMode.Overwrite) =>
        new FileOperationService(registry).ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [],
            SourceLocations = [source],
            DestinationLocation = destination,
            Options = new FileOperationOptions(),
        }, null, new Resolver(conflict));

    [Fact]
    public async Task LocalToArchive_PublishesBeforeDeletingSource()
    {
        string zip = Zip("destination.zip");
        string input = Path.Combine(_folder, "source.txt");
        await File.WriteAllTextAsync(input, "moved");
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(archive);

        var result = await Move(registry, PanelLocation.Local(input), new PanelLocation(archive.SourceId, "/"));
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(input));
        Assert.NotNull(archive.GetItem("/source.txt"));
        Assert.Equal(1, result.MovedCount);
    }

    [Fact]
    public async Task ArchiveToLocal_DeletesOnlyAfterCopy()
    {
        string zip = Zip("source.zip", ("first.txt", "first"));
        string output = Path.Combine(_folder, "output");
        Directory.CreateDirectory(output);
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(archive);

        var result = await Move(registry,
            new PanelLocation(archive.SourceId, "/first.txt"), PanelLocation.Local(output));
        Assert.Empty(result.Errors);
        Assert.Equal("first", (await File.ReadAllTextAsync(Path.Combine(output, "first.txt"))).TrimStart('\uFEFF'));
        Assert.Null(archive.GetItem("/first.txt"));
    }

    [Fact]
    public async Task SameArchiveInTwoPanels_UsesOneMutationBatch()
    {
        string zip = Zip("two-panels.zip", ("first.txt", "first"));
        await using var first = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        await using var second = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(first);
        registry.TryAddOwned(second);

        var result = await Move(registry,
            new PanelLocation(first.SourceId, "/first.txt"), new PanelLocation(second.SourceId, "/renamed.txt"));
        Assert.Empty(result.Errors);
        Assert.NotNull(first.GetItem("/renamed.txt"));
        Assert.Null(first.GetItem("/first.txt"));
        Assert.Throws<IOException>(() => second.GetItem("/first.txt"));
    }

    [Fact]
    public async Task ArchiveToArchive_CopyThenDelete()
    {
        string a = Zip("archive-a.zip", ("data.txt", "data"));
        string b = Zip("archive-b.zip");
        await using var source = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(a), Path.GetFileName(a));
        await using var destination = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(b), Path.GetFileName(b));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(source);
        registry.TryAddOwned(destination);

        var result = await Move(registry,
            new PanelLocation(source.SourceId, "/data.txt"), new PanelLocation(destination.SourceId, "/"));
        Assert.Empty(result.Errors);
        Assert.Null(source.GetItem("/data.txt"));
        Assert.NotNull(destination.GetItem("/data.txt"));
    }

    [Fact]
    public async Task ArchiveToLocal_PartialDirectorySkipPreservesSkippedChild()
    {
        string zip = Zip("partial.zip", ("tree/a.txt", "a"), ("tree/b.txt", "b"));
        string output = Path.Combine(_folder, "partial-output");
        Directory.CreateDirectory(Path.Combine(output, "tree"));
        await File.WriteAllTextAsync(Path.Combine(output, "tree", "a.txt"), "destination");
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(archive);

        var result = await Move(registry, new PanelLocation(archive.SourceId, "/tree"),
            PanelLocation.Local(output), ConflictDecisionMode.Skip);
        Assert.Empty(result.Errors);
        Assert.True(result.PartiallyMoved);
        Assert.NotNull(archive.GetItem("/tree/a.txt"));
        Assert.Null(archive.GetItem("/tree/b.txt"));
        Assert.Equal("b", (await File.ReadAllTextAsync(Path.Combine(output, "tree", "b.txt"))).TrimStart('\\uFEFF'));
    }

    [Fact]
    public async Task ArchiveToLocal_SkippedFileIsNotDeleted()
    {
        string zip = Zip("skip.zip", ("same.txt", "source"));
        string output = Path.Combine(_folder, "output");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "same.txt"), "destination");
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(zip), Path.GetFileName(zip));
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.TryAddOwned(archive);

        var result = await Move(registry,
            new PanelLocation(archive.SourceId, "/same.txt"), PanelLocation.Local(output),
            ConflictDecisionMode.Skip);
        Assert.Empty(result.Errors);
        Assert.NotNull(archive.GetItem("/same.txt"));
        Assert.Equal(0, result.MovedCount);
        Assert.True(result.SkippedCount > 0);
    }
}
