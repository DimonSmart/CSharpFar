using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class SafeFilePublicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "publication-" + Guid.NewGuid().ToString("N"));

    public SafeFilePublicationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReplacementFailure_RestoresOriginal()
    {
        var (target, staging) = LocalFiles();
        Assert.Throws<IOException>(() => SafeLocalFilePublisher.Publish(staging, target,
            new SafeLocalFilePublisher.PublicationOperations { Replace = FailAfterBackup }));
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal("new", File.ReadAllText(staging));
        Assert.Empty(Directory.GetFiles(_root, "*.backup"));
    }

    [Fact]
    public void RestoreFailure_PreservesBackupAndBothErrors()
    {
        var (target, staging) = LocalFiles();
        var error = Assert.Throws<IOException>(() => SafeLocalFilePublisher.Publish(staging, target,
            new SafeLocalFilePublisher.PublicationOperations
            {
                Replace = FailAfterBackup,
                Move = (_, _) => throw new IOException("restore"),
            }));
        string backup = Assert.Single(Directory.GetFiles(_root, "*.backup"));
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Contains(backup, error.Message);
        Assert.Equal(2, Assert.IsType<AggregateException>(error.InnerException).InnerExceptions.Count);
        Assert.Equal("new", File.ReadAllText(staging));
    }

    [Fact]
    public void CleanupFailure_IsSuccessWithRetainedBackup()
    {
        var (target, staging) = LocalFiles();
        string? backup = SafeLocalFilePublisher.Publish(staging, target,
            new SafeLocalFilePublisher.PublicationOperations { Delete = _ => throw new IOException("cleanup") });
        Assert.NotNull(backup);
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void LateDestination_IsNeverOverwritten()
    {
        string target = Path.Combine(_root, "target");
        string staging = SafeLocalFilePublisher.CreateStagingPath(target);
        File.WriteAllText(staging, "new");
        File.WriteAllText(target, "late");
        Assert.Throws<IOException>(() => SafeLocalFilePublisher.PublishChecked(staging, target, null));
        Assert.Equal("late", File.ReadAllText(target));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("close")]
    [InlineData("size")]
    [InlineData("rename")]
    public async Task ProviderFailure_DoesNotPublishAndCleansStaging(string fault)
    {
        var remote = new StagedSource { Fault = fault };
        await Assert.ThrowsAsync<IOException>(() => Write(remote));
        Assert.Null(remote.ReadText("/target"));
        Assert.Empty(remote.EnumerateDirectory("/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelBeforePublication_RespectsPartialPolicy(bool keep)
    {
        using var cts = new CancellationTokenSource();
        var remote = new StagedSource { OnClose = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(remote, keep, cts.Token));
        Assert.Null(remote.ReadText("/target"));
        if (keep)
        {
            var partial = Assert.Single(remote.EnumerateDirectory("/"));
            Assert.EndsWith(".tmp", partial.Name);
            Assert.Equal("data", remote.ReadText(partial.FullPath));
        }
        else Assert.Empty(remote.EnumerateDirectory("/"));
    }

    [Fact]
    public async Task ProviderCreate_UsesStagingAndPublishesCompleteBytes()
    {
        var remote = new StagedSource();
        Assert.Null(await Write(remote));
        Assert.Equal("data", remote.ReadText("/target"));
        Assert.Single(remote.EnumerateDirectory("/"));
    }

    [Fact]
    public async Task LateRemoteDestination_IsNotOverwritten()
    {
        var remote = new StagedSource { Fault = "late" };
        await Assert.ThrowsAsync<UnknownFilePublicationException>(() => Write(remote));
        Assert.Equal("external", remote.ReadText("/target"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossProviderMove_DeletesSourceOnlyAfterConfirmedPublication(bool lostReply)
    {
        var remote = new StagedSource { Fault = lostReply ? "lost" : "" };
        string path = Path.Combine(_root, "target");
        File.WriteAllText(path, "data");
        var registry = new FilePanelSourceRegistry([new LocalFilePanelSource(new FileSystemService()), remote]);
        var result = await new FileOperationService(registry).ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [PanelLocation.Local(path)],
            Destination = new PanelLocation(remote.SourceId, "/"),
            Options = new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.Overwrite },
        }, null, new OverwriteResolver());
        Assert.Equal("data", remote.ReadText("/target"));
        Assert.Equal(lostReply, result.PublicationUnknown);
        Assert.Equal(lostReply, File.Exists(path));
        Assert.Equal(lostReply ? 0 : 1, result.MovedCount);
    }

    private (string Target, string Staging) LocalFiles()
    {
        string target = Path.Combine(_root, "target");
        string staging = SafeLocalFilePublisher.CreateStagingPath(target);
        File.WriteAllText(target, "old");
        File.WriteAllText(staging, "new");
        return (target, staging);
    }

    private static void FailAfterBackup(string staging, string target, string backup)
    {
        File.Move(target, backup);
        throw new IOException("publish");
    }

    private static Task<string?> Write(StagedSource remote, bool keep = false, CancellationToken ct = default) =>
        SafeFileWriteService.WriteProviderAsync(remote, "/target", null, 4, async stream =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("data"), ct);
            return 4;
        }, keep, ct);

    private sealed class StagedSource : MemoryFilePanelView, ISafeNewFilePublicationProvider
    {
        public StagedSource() : base(new PanelSourceId("staged-test")) { }
        public string Fault { get; init; } = "";
        public Action? OnClose { get; init; }

        public Task<Stream> OpenStagingWriteAsync(string path, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (GetItem(path) is not null) throw new IOException("exists");
            SeedFile(path, "");
            return Task.FromResult<Stream>(new FaultStream(Fault, bytes =>
            {
                SeedFile(path, Fault == "size" ? "x" : Encoding.UTF8.GetString(bytes));
                OnClose?.Invoke();
            }));
        }

        public async Task RenameWithoutOverwriteAsync(string source, string destination, CancellationToken ct = default)
        {
            if (Fault == "rename") throw new IOException("rename rejected");
            if (Fault == "late") SeedFile(destination, "external");
            if (GetItem(destination) is not null) throw new IOException("exists");
            await RenameAsync(source, destination, ct);
            if (Fault == "lost") throw new IOException("reply lost");
        }
    }

    private sealed class FaultStream(string fault, Action<byte[]> close) : MemoryStream
    {
        private bool _closed;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (fault == "write") throw new IOException("write");
            return base.WriteAsync(buffer, ct);
        }
        public override Task FlushAsync(CancellationToken ct)
        {
            if (fault == "flush") throw new IOException("flush");
            return base.FlushAsync(ct);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                close(ToArray());
                if (fault == "close") throw new IOException("close");
            }
            base.Dispose(disposing);
        }
    }

    private sealed class OverwriteResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }
}
