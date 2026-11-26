using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

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
            Sources = [PanelLocation.Demo("/source.txt")],
            Destination = PanelLocation.Demo("/target.txt"),
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MidstreamFailure_OnlyRetainsOperationOwnedPartialWhenRequested(bool keepPartial)
    {
        string source = Write(_src, "partial.bin", new string('Z', 2 * 1024 * 1024));
        string destination = Write(_dst, "partial.bin", "ORIGINAL");
        var dependencies = FileOperationServiceDependencies.Default with
        {
            OpenFileStream = (path, mode, access, share, size, options) =>
            {
                Stream stream = FileOperationServiceDependencies.Default.OpenFileStream(
                    path, mode, access, share, size, options);
                return path == source && access == FileAccess.Read
                    ? new FailAfterFirstRead(stream) : stream;
            },
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
        string[] partials = Directory.GetFiles(_dst, "*.partial*", SearchOption.AllDirectories);
        Assert.Equal(keepPartial ? 1 : 0, partials.Length);
        if (keepPartial) Assert.NotEmpty(File.ReadAllBytes(partials[0]));
    }

    [Fact]
    public async Task TransactionalProvider_PublishesOverwriteOnlyOnSuccessfulCommit()
    {
        var provider = new InMemoryBatchFilePanelSource();
        provider.SeedDirectory("/src");
        provider.SeedDirectory("/dst");
        provider.SeedFile("/src/item.txt", "NEW");
        provider.SeedFile("/dst/item.txt", "ORIGINAL");
        var registry = new FilePanelSourceRegistry([provider]);
        var service = new FileOperationService(registry);
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [new PanelLocation(provider.SourceId, "/src/item.txt")],
            Destination = new PanelLocation(provider.SourceId, "/dst/item.txt"),
            Options = new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.Overwrite },
        };

        provider.FailCommit = true;
        await Assert.ThrowsAsync<IOException>(() =>
            service.ExecuteAsync(request, null, new OverwriteResolver()));
        Assert.Equal("ORIGINAL", provider.ReadText("/dst/item.txt"));
        Assert.Equal("NEW", provider.ReadText("/src/item.txt"));

        provider.FailCommit = false;
        FileOperationResult result = await service.ExecuteAsync(request, null, new OverwriteResolver());
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("NEW", provider.ReadText("/dst/item.txt"));
        Assert.Equal(1, provider.Committed);
    }

    [Fact]
    public async Task MoveFallback_PublicationFailurePreservesBothOriginalAndSource()
    {
        string source = Write(_src, "move.txt", "NEW");
        string destination = Write(_dst, "move.txt", "ORIGINAL");
        var dependencies = FileOperationServiceDependencies.Default with
        {
            ForceMoveFallback = static (_, _) => true,
            PublishNewLocalFile = (_, _) => throw new IOException("Simulated staging publication error"),
        };
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [PanelLocation.Local(source)],
            Destination = PanelLocation.Local(_dst),
            Options = new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.Replace },
        };
        await Assert.ThrowsAsync<IOException>(() =>
            new FileOperationService(dependencies).ExecuteAsync(request, null, new OverwriteResolver()));
        Assert.Equal("ORIGINAL", File.ReadAllText(destination));
        Assert.Equal("NEW", File.ReadAllText(source));
    }

    [Fact]
    public async Task FileLink_CopyLinkAndCopyContentsHaveDistinctSemantics()
    {
        Write(_src, "original.txt", "PAYLOAD");
        string alias = Path.Combine(_src, "alias.txt");
        if (!TryLink(() => File.CreateSymbolicLink(alias, "original.txt"))) return;

        var service = new FileOperationService();
        string copiedLink = Path.Combine(_dst, "alias.txt");
        FileOperationResult linkResult = await service.ExecuteAsync(
            Request(alias, copiedLink), null, new OverwriteResolver());
        Assert.Equal(1, linkResult.CopiedCount);
        Assert.Equal("original.txt", new FileInfo(copiedLink).LinkTarget);

        string copiedContents = Path.Combine(_dst, "copy.txt");
        FileOperationResult contentsResult = await service.ExecuteAsync(
            Request(alias, copiedContents) with
            {
                Options = new FileOperationOptions
                {
                    SymlinkMode = SymlinkCopyMode.CopyTargetContents,
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            }, null, new OverwriteResolver());
        Assert.Equal(1, contentsResult.CopiedCount);
        Assert.Equal("PAYLOAD", File.ReadAllText(copiedContents));
        Assert.Null(new FileInfo(copiedContents).LinkTarget);
    }

    [Fact]
    public async Task CopyTargetContents_AllowsTwoAliasesWithoutCycle()
    {
        string shared = Path.Combine(_src, "shared");
        Directory.CreateDirectory(shared);
        Write(shared, "contents.txt", "VALUE");
        string a = Path.Combine(_src, "alias-a");
        string b = Path.Combine(_src, "alias-b");
        if (!TryLink(() => Directory.CreateSymbolicLink(a, "shared")) ||
            !TryLink(() => Directory.CreateSymbolicLink(b, "shared"))) return;

        var request = Request(_src, _dst) with
        {
            Options = new FileOperationOptions { SymlinkMode = SymlinkCopyMode.CopyTargetContents },
        };
        var service = new FileOperationService();
        Assert.Equal(7, service.BuildPlan(request).Items.Count);
        FileOperationResult result = await service.ExecuteAsync(request, null, new OverwriteResolver());
        Assert.Equal(3, result.CopiedCount);
        string destination = Path.Combine(_dst, "src");
        Assert.Equal("VALUE", File.ReadAllText(Path.Combine(destination, "alias-a", "contents.txt")));
        Assert.Equal("VALUE", File.ReadAllText(Path.Combine(destination, "alias-b", "contents.txt")));
    }

    [Fact]
    public void CopyTargetContents_IndirectLinkLoopFailsBeforeWriting()
    {
        string a = Path.Combine(_src, "a");
        string b = Path.Combine(_src, "b");
        string c = Path.Combine(_src, "c");
        if (!TryLink(() => Directory.CreateSymbolicLink(a, "b")) ||
            !TryLink(() => Directory.CreateSymbolicLink(b, "c")) ||
            !TryLink(() => Directory.CreateSymbolicLink(c, "a"))) return;

        var request = Request(_src, _dst) with
        {
            Options = new FileOperationOptions { SymlinkMode = SymlinkCopyMode.CopyTargetContents },
        };
        Assert.ThrowsAny<IOException>(() => new FileOperationService().BuildPlan(request));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dst));
    }

    [Fact]
    public async Task CopyLink_MaskedCopyKeepsLinkWithoutWalkingItsTree()
    {
        string folder = Path.Combine(_src, "real");
        Directory.CreateDirectory(folder);
        Write(folder, "data.txt", "data");
        string link = Path.Combine(_src, "shortcut.txt");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, "real"))) return;

        var request = Request(_src, _dst) with
        {
            Options = new FileOperationOptions { FileMask = "*.txt", SymlinkMode = SymlinkCopyMode.CopyLink },
        };
        var service = new FileOperationService();
        FileOperationPlan plan = service.BuildPlan(request);
        Assert.Equal(2, plan.Items.Count);
        Assert.Contains(plan.Items, item => item.Source.SourcePath == link && !item.IsDirectory);

        FileOperationResult result = await service.ExecuteAsync(request, null, new OverwriteResolver());
        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("real", new DirectoryInfo(Path.Combine(_dst, "src", "shortcut.txt")).LinkTarget);
    }

    private FileOperationRequest Request(string source, string destination, CopyMode copyMode = CopyMode.Normal) =>
        new()
        {
            Kind = FileOperationKind.Copy,
            Sources = [PanelLocation.Local(source)],
            Destination = PanelLocation.Local(destination),
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
