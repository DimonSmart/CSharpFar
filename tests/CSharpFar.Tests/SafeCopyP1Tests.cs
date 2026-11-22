using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class SafeCopyP1Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "csharpfar-safe-copy-" + Guid.NewGuid().ToString("N"));
    private readonly string _src;
    private readonly string _dst;

    public SafeCopyP1Tests()
    {
        _src = Path.Combine(_root, "src");
        _dst = Path.Combine(_root, "dst");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_dst);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(CopyMode.Normal)]
    [InlineData(CopyMode.Reliable)]
    [InlineData(CopyMode.FastSalvage)]
    public async Task ExistingFile_IsPublishedOnlyAfterSuccessfulCopy(CopyMode mode)
    {
        string source = Write(_src, "data.bin", "NEW");
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        var request = Request(source, destination, mode);
        FileOperationResult result = await new FileOperationService().ExecuteAsync(request, null, new OverwriteResolver());
        Assert.Empty(result.Errors);
        Assert.Equal("NEW", File.ReadAllText(destination));
        Assert.Equal("NEW", File.ReadAllText(source));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(CopyMode.Normal)]
    [InlineData(CopyMode.FastSalvage)]
    public async Task SourceOpenFailure_LeavesOriginalBytesUntouched(CopyMode mode)
    {
        string source = Write(_src, "data.bin", "NEW");
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        var dependencies = FileOperationServiceDependencies.Default with
        {
            OpenFileStream = (path, fileMode, access, share, bufferSize, options) =>
                path == source && access == FileAccess.Read
                    ? throw new IOException("Simulated source error")
                    : FileOperationServiceDependencies.Default.OpenFileStream(path, fileMode, access, share, bufferSize, options),
        };
        if (mode == CopyMode.FastSalvage)
        {
            FileOperationResult skipped = await new FileOperationService(dependencies)
                .ExecuteAsync(Request(source, destination, mode), null, new OverwriteResolver());
            Assert.Equal(1, skipped.SkippedCount);
            Assert.Equal(1, skipped.FailedCount);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() =>
                new FileOperationService(dependencies).ExecuteAsync(
                    Request(source, destination, mode), null, new OverwriteResolver()));
        }
        Assert.Equal("ORIGINAL", File.ReadAllText(destination));
        Assert.Equal("NEW", File.ReadAllText(source));
        AssertNoStaging();
    }

    [Fact]
    public async Task PartialReadFailure_PreservesDestinationAndRemovesStaging()
    {
        string source = Write(_src, "data.bin", new string('X', 2 * 1024 * 1024));
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        var dependencies = FileOperationServiceDependencies.Default with
        {
            OpenFileStream = (path, mode, access, share, bufferSize, options) =>
            {
                Stream stream = FileOperationServiceDependencies.Default.OpenFileStream(
                    path, mode, access, share, bufferSize, options);
                return path == source && access == FileAccess.Read
                    ? new FailAfterFirstRead(stream) : stream;
            },
        };
        await Assert.ThrowsAsync<IOException>(() =>
            new FileOperationService(dependencies).ExecuteAsync(Request(source, destination), null, new OverwriteResolver()));
        Assert.Equal("ORIGINAL", File.ReadAllText(destination));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailure_PreservesOriginalAndRespectsPartialPolicy(bool keepPartial)
    {
        string source = Write(_src, "data.bin", "NEW");
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        var dependencies = FileOperationServiceDependencies.Default with
        {
            PublishLocalFile = (_, _) => throw new IOException("Simulated publication failure"),
        };
        var request = Request(source, destination) with
        {
            Options = new FileOperationOptions
            {
                DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                KeepPartialFilesOnError = keepPartial,
            },
        };
        await Assert.ThrowsAsync<IOException>(() =>
            new FileOperationService(dependencies).ExecuteAsync(request, null, new OverwriteResolver()));
        Assert.Equal("ORIGINAL", File.ReadAllText(destination));
        Assert.Equal("NEW", File.ReadAllText(source));
        Assert.Equal(keepPartial, Directory.EnumerateFiles(_dst, "*.partial*").Any());
        if (!keepPartial) AssertNoStaging();
    }

    [Fact]
    public async Task CancelDuringCopy_DoesNotPublishPartialBytes()
    {
        string source = Write(_src, "data.bin", new string('X', 3 * 1024 * 1024));
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterFirstChunk(cancellation);
        FileOperationResult result = await new FileOperationService().ExecuteAsync(
            Request(source, destination), progress, new OverwriteResolver(), cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.Equal("ORIGINAL", File.ReadAllText(destination));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(ConflictDecisionMode.Skip, "ORIGINAL")]
    [InlineData(ConflictDecisionMode.Overwrite, "NEW")]
    [InlineData(ConflictDecisionMode.OnlyNewer, "NEW")]
    public async Task ConflictPolicy_DoesNotMutateDestinationBeforeDecision(
        ConflictDecisionMode decision, string expected)
    {
        string source = Write(_src, "data.bin", "NEW");
        string destination = Write(_dst, "data.bin", "ORIGINAL");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow.AddDays(-1));
        var request = Request(source, destination) with
        {
            Options = new FileOperationOptions { DefaultConflictDecision = decision },
        };
        await new FileOperationService().ExecuteAsync(request, null, new OverwriteResolver());
        Assert.Equal(expected, File.ReadAllText(destination));
        AssertNoStaging();
    }

    [Fact]
    public async Task ProviderWithoutBatch_RejectsOverwriteWithoutDeletingTarget()
    {
        string fixture = Path.Combine(_root, "demo");
        Write(fixture, "source.txt", "NEW");
        Write(fixture, "target.txt", "ORIGINAL");
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var service = new FileOperationService(new FilePanelSourceRegistry([source]));
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            SourceLocations = [PanelLocation.Demo("/source.txt")],
            Sources = [],
            DestinationLocation = PanelLocation.Demo("/target.txt"),
            Options = new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.Overwrite },
        };
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.ExecuteAsync(request, null, new OverwriteResolver()));
        await using Stream stream = await source.OpenReadAsync("/target.txt");
        using var reader = new StreamReader(stream);
        Assert.Equal("ORIGINAL", await reader.ReadToEndAsync());
    }

    [Fact]
    public void CopyLink_PreviewTreatsDirectoryLinkAsSingleElement()
    {
        string folder = Path.Combine(_src, "tree");
        Directory.CreateDirectory(folder);
        Write(folder, "data.txt", "contents");
        string link = Path.Combine(_src, "shortcut");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, "tree"))) return;
        var service = new FileOperationService();
        var plan = service.BuildPlan(Request(link, Path.Combine(_dst, "shortcut")));
        Assert.Single(plan.Items);
        Assert.False(plan.Items[0].IsDirectory);
    }

    [Fact]
    public async Task CopyLink_DoesNotFollowDirectoryOrBrokenFileLinks()
    {
        string folder = Path.Combine(_src, "tree");
        Directory.CreateDirectory(folder);
        Write(folder, "data.txt", "contents");
        string link = Path.Combine(_src, "shortcut");
        string broken = Path.Combine(_src, "broken");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, "tree")) ||
            !TryLink(() => File.CreateSymbolicLink(broken, "not-found.txt"))) return;
        var service = new FileOperationService();
        await service.ExecuteAsync(Request(link, _dst), null, new OverwriteResolver());
        await service.ExecuteAsync(Request(broken, Path.Combine(_dst, "broken")), null, new OverwriteResolver());
        Assert.Equal("tree", new DirectoryInfo(Path.Combine(_dst, "shortcut")).LinkTarget);
        Assert.Equal("not-found.txt", new FileInfo(Path.Combine(_dst, "broken")).LinkTarget);
    }

    [Fact]
    public void CopyTargetContents_DetectsParentLinkCycle()
    {
        string folder = Path.Combine(_src, "tree");
        Directory.CreateDirectory(folder);
        string link = Path.Combine(folder, "up");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, ".."))) return;
        var request = Request(_src, _dst) with
        {
            Options = new FileOperationOptions { SymlinkMode = SymlinkCopyMode.CopyTargetContents },
        };
        Assert.Throws<IOException>(() => new FileOperationService().BuildPlan(request));
    }

    [Fact]
    public void CopyTargetContents_MaskedPlanAlsoDetectsCycle()
    {
        string folder = Path.Combine(_src, "tree");
        Directory.CreateDirectory(folder);
        string link = Path.Combine(folder, "up");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, ".."))) return;
        var request = Request(_src, _dst) with
        {
            Options = new FileOperationOptions
            {
                SymlinkMode = SymlinkCopyMode.CopyTargetContents,
                FileMask = "*.txt",
            },
        };
        Assert.Throws<IOException>(() => new FileOperationService().BuildPlan(request));
    }

    private FileOperationRequest Request(string source, string destination, CopyMode copyMode = CopyMode.Normal) =>
        new()
        {
            Kind = FileOperationKind.Copy,
            Sources = [source],
            Destination = destination,
            Options = new FileOperationOptions
            {
                CopyMode = copyMode,
                DefaultConflictDecision = ConflictDecisionMode.Overwrite,
            },
        };

    private static string Write(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private void AssertNoStaging() =>
        Assert.Empty(Directory.EnumerateFiles(_dst, "*.csharpfar-*.tmp*", SearchOption.AllDirectories));

    private static bool TryLink(Action create)
    {
        try { create(); return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class OverwriteResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private sealed class CancelAfterFirstChunk(CancellationTokenSource cancellation)
        : IProgress<FileOperationProgress>
    {
        public void Report(FileOperationProgress value)
        {
            if (value.Phase == FileOperationPhase.Copying && value.CurrentBytesDone > 0)
                cancellation.Cancel();
        }
    }

    private sealed class FailAfterFirstRead(Stream inner) : Stream
    {
        private bool _read;
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_read) throw new IOException("Simulated midstream read failure");
            _read = true;
            return inner.ReadAsync(buffer, token);
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
