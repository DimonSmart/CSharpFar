using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;

namespace CSharpFar.Tests;

public sealed class ArchiveTarWriteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(),
        "csharpfar-tar-write-" + Guid.NewGuid().ToString("N"));

    public ArchiveTarWriteTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static LocalFilePanelSource Local() => new(new FileSystemService());

    private string Create(bool gzip)
    {
        string name = "example" + (gzip ? ".tar.gz" : ".tar");
        string file = Path.Combine(_folder, name);
        using var output = File.Create(file);
        using var compressor = gzip ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : null;
        using var writer = new TarWriter(compressor ?? output, TarEntryFormat.Pax, leaveOpen: true);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("original"));
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "old.txt") { DataStream = content };
        writer.WriteEntry(entry);
        return file;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_AddDeleteRenameAndExplicitDirectory(bool gzip)
    {
        string file = Create(gzip);
        await using (var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(file), Path.GetFileName(file)))
        {
            Assert.True(archive.Capabilities.HasFlag(PanelProviderCapabilities.OpenWrite));
            await using var batch = await archive.BeginBatchAsync(new FileOperationPlan([]));
            await batch.CreateDirectoryAsync("/empty");
            await batch.RenameAsync("/old.txt", "/renamed.txt");
            await using (var write = await batch.OpenWriteAsync("/new.txt", overwrite: false))
                await write.WriteAsync(Encoding.UTF8.GetBytes("new"));
            await batch.CommitAsync();
            Assert.NotNull(archive.GetItem("/renamed.txt"));
            Assert.Null(archive.GetItem("/old.txt"));
        }
        await using var reopened = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(file), Path.GetFileName(file));
        Assert.Empty(reopened.EnumerateDirectory("/empty"));
        await using var input = await reopened.OpenReadAsync("/new.txt");
        using var reader = new StreamReader(input);
        Assert.Equal("new", await reader.ReadToEndAsync());
    }
}
