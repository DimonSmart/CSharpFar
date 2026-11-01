using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class ArchiveProviderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "csharpfar-archives-test-" + Guid.NewGuid().ToString("N"));

    public ArchiveProviderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Zip(string name, params (string Path, string? Content)[] entries)
    {
        string file = Path.Combine(_folder, name);
        using (var zip = new ZipArchive(File.Create(file), ZipArchiveMode.Create))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                if (content is not null)
                {
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(content);
                }
            }
        }
        return file;
    }

    private static LocalFilePanelSource Local() => new(new FileSystemService());

    [Fact]
    public async Task Zip_ImplicitDirectories_Unicode_EmptyDirectory_AndStreamingRead()
    {
        string path = Zip("sample.zip", ("src/core/main.cs", "hello"), ("Русский/имя.txt", "мир"), ("empty/", null));
        await using var source = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), "sample.zip");

        Assert.Equal("ZIP", source.Format);
        Assert.Equal(PanelProviderCapabilities.Enumerate |
                     PanelProviderCapabilities.OpenRead |
                     PanelProviderCapabilities.CopyFrom |
                     PanelProviderCapabilities.Refresh, source.Capabilities);
        Assert.Equal(["empty", "src", "Русский"], source.EnumerateDirectory("/").Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Single(source.EnumerateDirectory("/src"));
        Assert.Equal("main.cs", Assert.Single(source.EnumerateDirectory("/src/core")).Name);
        Assert.Empty(source.EnumerateDirectory("/empty"));

        await using var stream = await source.OpenReadAsync("/Русский/имя.txt");
        using var reader = new StreamReader(stream);
        Assert.Equal("мир", await reader.ReadToEndAsync());
        await Assert.ThrowsAsync<NotSupportedException>(
            () => source.CreateDirectoryAsync("/new"));
    }

    [Fact]
    public async Task Zip_CopyFolderAndEmptyDirectory_ThroughFileOperationService()
    {
        string path = Zip("copy.zip", ("tree/a.txt", "a"), ("tree/sub/b.txt", "b"), ("empty/", null));
        await using var archive = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), "copy.zip");
        var registry = new FilePanelSourceRegistry([Local()]);
        Assert.True(registry.TryAddOwned(archive));
        string destination = Path.Combine(_folder, "output");
        Directory.CreateDirectory(destination);
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [],
            SourceLocations = [new PanelLocation(archive.SourceId, "/tree"), new PanelLocation(archive.SourceId, "/empty")],
            DestinationLocation = PanelLocation.Local(destination),
            Options = new FileOperationOptions(),
        };
        FileOperationResult result = await new FileOperationService(registry)
            .ExecuteAsync(request, null, new OverwriteConflicts());
        Assert.Empty(result.Errors);
        Assert.Equal("a", File.ReadAllText(Path.Combine(destination, "tree", "a.txt")).TrimStart('\uFEFF'));
        Assert.Equal("b", File.ReadAllText(Path.Combine(destination, "tree", "sub", "b.txt")).TrimStart('\uFEFF'));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Zip_PanelControllerUsesOrdinarySourceAndNavigation()
    {
        string path = Zip("panel.zip", ("a/b/file.txt", "yes"));
        await using var archive = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), "panel.zip");
        var registry = new FilePanelSourceRegistry([Local()]);
        registry.Add(archive);
        var controller = new PanelController(new PanelViewBuilder(new FileSystemService(), new PanelSortService(), sources: registry));
        var state = new FilePanelState();
        var options = new AppSettings.PanelOptionsSettings();
        controller.LoadLocation(state, new PanelLocation(archive.SourceId, "/"), options);
        Assert.Equal("a", Assert.Single(state.Items).Name);
        controller.LoadLocation(state, new PanelLocation(archive.SourceId, "/a/b"), options);
        Assert.Contains(state.Items, i => i.Name == "file.txt");
    }

    [Fact]
    public async Task Zip_NestedArchiveFromNonSeekableSource_AndResourceCleanup()
    {
        byte[] inner;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("inside.txt").Open(), Encoding.UTF8);
                writer.Write("inside");
            }
            inner = memory.ToArray();
        }
        string outerPath = Path.Combine(_folder, "outer.zip");
        using (var zip = new ZipArchive(File.Create(outerPath), ZipArchiveMode.Create))
        {
            using var output = zip.CreateEntry("inner.zip").Open();
            output.Write(inner);
        }
        await using var outer = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(outerPath), "outer.zip");
        await using var nested = await ArchiveFilePanelSource.CreateAsync(
            outer, new PanelLocation(outer.SourceId, "/inner.zip"), "inner.zip");
        Assert.Equal(2, nested.Depth);
        await using var content = await nested.OpenReadAsync("/inside.txt");
        using var reader = new StreamReader(content);
        Assert.Equal("inside", await reader.ReadToEndAsync());

        byte[] outerBytes = await File.ReadAllBytesAsync(outerPath);
        var remote = new NonSeekableSource(outerBytes);
        await using var spooled = await ArchiveFilePanelSource.CreateAsync(
            remote, new PanelLocation(remote.SourceId, "/outer.zip"), "outer.zip");
        Assert.Contains(spooled.EnumerateDirectory("/"), item => item.Name == "inner.zip");
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/drive.txt")]
    [InlineData("x/../../escape.txt")]
    [InlineData("a\\b.txt")]
    public async Task Zip_UnsafePathsAreRejected(string entryName)
    {
        string path = Zip("unsafe.zip", (entryName, "unsafe"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), "unsafe.zip"));
    }

    [Fact]
    public async Task Zip_DuplicatesAndCorruptInputAreRejected()
    {
        string duplicate = Zip("duplicate.zip", ("same.txt", "a"), ("same.txt", "b"));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(duplicate), "duplicate.zip"));
        string corrupt = Path.Combine(_folder, "corrupt.zip");
        await File.WriteAllTextAsync(corrupt, "not a zip");
        await Assert.ThrowsAnyAsync<Exception>(
            () => ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(corrupt), "corrupt.zip"));
    }

    [Fact]
    public async Task Archive_ExternalModificationIsDetected()
    {
        string path = Zip("changed.zip", ("file.txt", "before"));
        await using var archive = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), "changed.zip");
        await File.AppendAllTextAsync(path, "trailing data");
        Assert.Throws<IOException>(() => archive.EnumerateDirectory("/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tar_AndTarGz_ReadEntries(bool gzip)
    {
        string path = Path.Combine(_folder, gzip ? "archive.tar.gz" : "archive.tar");
        using (var file = File.Create(path))
        {
            using var compression = gzip ? new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true) : null;
            using var writer = new TarWriter((Stream?)compression ?? file, leaveOpen: true);
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "folder/file.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("hello")),
            });
        }
        await using var source = await ArchiveFilePanelSource.CreateAsync(Local(), PanelLocation.Local(path), Path.GetFileName(path));
        Assert.Equal(gzip ? "TAR.GZ" : "TAR", source.Format);
        Assert.Equal("folder", Assert.Single(source.EnumerateDirectory("/")).Name);
        await using var stream = await source.OpenReadAsync("/folder/file.txt");
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Handler_ClaimsSupportedExtensionsAndDoesNotFallBackOnCorruption()
    {
        var handler = new ArchiveFileOpenHandler();
        var item = new FilePanelItem { Name = "invalid.zip", FullPath = "/invalid.zip", IsDirectory = false };
        Assert.True(handler.CanOpen(item));
        Assert.False(handler.CanOpen(new FilePanelItem { Name = "file.txt", FullPath = "/file.txt", IsDirectory = false }));
        var source = new NonSeekableSource(Encoding.UTF8.GetBytes("not an archive"));
        var result = await handler.OpenAsync(new FileOpenContext
        {
            Item = item,
            Location = new PanelLocation(source.SourceId, "/invalid.zip"),
            Source = source,
            TargetPanelSide = PanelSide.Left,
            Application = null!, Settings = null!, Credentials = null!, Ui = null!,
        }, CancellationToken.None);
        Assert.Equal(PluginFileOpenResultKind.Failed, result.Kind);
    }

    private sealed class OverwriteConflicts : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private sealed class NonSeekableSource(byte[] bytes) : IFilePanelSource
    {
        public PanelSourceId SourceId { get; } = new("test:nonseekable:" + Guid.NewGuid().ToString("N"));
        public string DisplayName => "Test";
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.OpenRead;
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public string NormalizePath(string path) => path;
        public bool IsRootPath(string path) => path == "/";
        public string? GetParentPath(string path) => "/";
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string path, CancellationToken ct = default) => [];
        public FilePanelItem? GetItem(string path, CancellationToken ct = default) => null;
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new ReadOnlyForwardStream(bytes));
        public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RenameAsync(string path, string newPath, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ReadOnlyForwardStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
