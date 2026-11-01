using System.IO.Compression;
using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;

namespace CSharpFar.Tests;

public sealed class ArchiveWritableTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "csharpfar-write-" + Guid.NewGuid().ToString("N"));

    public ArchiveWritableTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static LocalFilePanelSource Local() => new(new FileSystemService());

    private string Zip(params (string Name, string Text)[] files)
    {
        string path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(text);
        }
        return path;
    }

    private sealed class OverwriteResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict _) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    [Fact]
    public async Task Zip_Batch_AddReplaceDeleteRename_ThenReopen()
    {
        string path = Zip(("original.txt", "original"), ("folder/child.txt", "child"));
        await using (var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path)))
        {
            Assert.True(archive.Capabilities.HasFlag(PanelProviderCapabilities.OpenWrite));
            var location = new PanelLocation(archive.SourceId, "/");
            await using var batch = await archive.BeginBatchAsync(new FileOperationPlan([]));
            await batch.CreateDirectoryAsync("/empty");
            await batch.RenameAsync("/folder", "/renamed");
            await batch.DeleteAsync("/original.txt", recursive: false);
            await using (var write = await batch.OpenWriteAsync("/renamed/new.txt", overwrite: false))
            {
                await write.WriteAsync(Encoding.UTF8.GetBytes("hello"));
            }
            await batch.CommitAsync();
            Assert.Null(archive.GetItem("/folder"));
            Assert.NotNull(archive.GetItem("/renamed/new.txt"));
            Assert.Empty(archive.EnumerateDirectory("/empty"));
        }

        await using var reopened = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        Assert.Null(reopened.GetItem("/original.txt"));
        Assert.Equal(2, reopened.EnumerateDirectory("/renamed").Count);
        Assert.Empty(reopened.EnumerateDirectory("/empty"));
        await using var input = await reopened.OpenReadAsync("/renamed/new.txt");
        using var reader = new StreamReader(input);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Zip_FailedCommit_LeavesOriginalByteIdentical()
    {
        string path = Zip(("original.txt", "keep"));
        byte[] before = await File.ReadAllBytesAsync(path);
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        await using var batch = await archive.BeginBatchAsync(new FileOperationPlan([]));
        var unfinished = await batch.OpenWriteAsync("/new.txt", overwrite: true);
        await Assert.ThrowsAsync<IOException>(() => batch.CommitAsync());
        await batch.AbortAsync();
        await unfinished.DisposeAsync();
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Zip_SecondSessionCannotOverwriteFirstCommit()
    {
        string path = Zip(("original.txt", "keep"));
        await using var first = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        await using var second = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));

        await first.CreateDirectoryAsync("/saved");
        await Assert.ThrowsAsync<IOException>(() => second.BeginBatchAsync(
            new FileOperationPlan([])).AsTask());
        Assert.NotNull(first.GetItem("/saved"));
    }

    [Fact]
    public async Task Zip_LocalCopy_UsesOrdinaryFileOperationService()
    {
        string path = Zip(("old.txt", "old"));
        string file = Path.Combine(_folder, "added.txt");
        await File.WriteAllTextAsync(file, "new-content");

        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        var registry = new FilePanelSourceRegistry([Local()]);
        Assert.True(registry.TryAddOwned(archive));
        var result = await new FileOperationService(registry).ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [file],
            SourceLocations = [PanelLocation.Local(file)],
            DestinationLocation = new PanelLocation(archive.SourceId, "/"),
            Options = new FileOperationOptions(),
        }, null, new OverwriteResolver());

        Assert.Empty(result.Errors);
        Assert.NotNull(archive.GetItem("/added.txt"));
        await using var input = await archive.OpenReadAsync("/added.txt");
        using var reader = new StreamReader(input);
        Assert.Equal("new-content", await reader.ReadToEndAsync());
    }
}
