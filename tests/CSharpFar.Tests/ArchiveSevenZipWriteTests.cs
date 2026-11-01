using System.Text;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace CSharpFar.Tests;

public sealed class ArchiveSevenZipWriteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(),
        "csharpfar-7z-write-" + Guid.NewGuid().ToString("N"));

    public ArchiveSevenZipWriteTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task NonSolidLzma2_EmptyDirectoryReplaceAndDelete_ThroughRebuild()
    {
        string file = Path.Combine(_folder, "test.7z");
        using (var output = File.Create(file))
        using (var writer = WriterFactory.OpenWriter(
            output, ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2) { LeaveStreamOpen = true }))
        {
            writer.WriteDirectory("empty", DateTime.UtcNow);
            using var initial = new MemoryStream(Encoding.UTF8.GetBytes("before"));
            writer.Write("old.txt", initial, DateTime.UtcNow);
        }

        var local = new LocalFilePanelSource(new FileSystemService());
        await using (var archive = await ArchiveFilePanelSource.CreateAsync(
            local, PanelLocation.Local(file), Path.GetFileName(file)))
        {
            Assert.True(archive.Capabilities.HasFlag(PanelProviderCapabilities.OpenWrite));
            await using var batch = await archive.BeginBatchAsync(new FileOperationPlan([]));
            await batch.DeleteAsync("/old.txt", recursive: false);
            await batch.CreateDirectoryAsync("/new-empty");
            await using (var stream = await batch.OpenWriteAsync("/new.txt", overwrite: false))
                await stream.WriteAsync(Encoding.UTF8.GetBytes("after"));
            await batch.CommitAsync();
        }

        await using var reopened = await ArchiveFilePanelSource.CreateAsync(
            local, PanelLocation.Local(file), Path.GetFileName(file));
        Assert.Null(reopened.GetItem("/old.txt"));
        Assert.Empty(reopened.EnumerateDirectory("/empty"));
        Assert.Empty(reopened.EnumerateDirectory("/new-empty"));
        await using var result = await reopened.OpenReadAsync("/new.txt");
        using var reader = new StreamReader(result);
        Assert.Equal("after", await reader.ReadToEndAsync());
    }
}
