using System.Text;
using CSharpFar.Core.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;

namespace CSharpFar.Tests;

public sealed class NetworkPublicationIntegrationTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CSHARPFAR_PUBLICATION_INTEGRATION") == "1";
    private static string Env(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing integration setting: {name}");

    [Fact]
    public async Task Sftp_ExclusiveStagingAndNonOverwritingRename()
    {
        if (!Enabled) return;
        string root = Env("CSHARPFAR_SFTP_ROOT");
        using var source = new SftpFilePanelSource(new SftpConnectionInfo
        {
            Id = "publication-integration", DisplayName = "Loopback integration SFTP",
            Host = "127.0.0.1", Port = int.Parse(Env("CSHARPFAR_SFTP_PORT")),
            Username = Env("CSHARPFAR_TEST_USER"), RemoteRootPath = root,
        }, Env("CSHARPFAR_TEST_PASSWORD"), acceptHostKey: (_, _) => true);

        string staging = root + "/exclusive.tmp";
        string target = root + "/target";
        await using (var stream = await source.OpenStagingWriteAsync(staging))
            await stream.WriteAsync(Encoding.UTF8.GetBytes("stage"));
        await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await using var duplicate = await source.OpenStagingWriteAsync(staging);
        });
        await using (var stream = await source.OpenWriteAsync(target, overwrite: false))
            await stream.WriteAsync(Encoding.UTF8.GetBytes("original"));
        await Assert.ThrowsAnyAsync<IOException>(() => source.RenameWithoutOverwriteAsync(staging, target));
        Assert.Equal("original", await Read(source, target));
        Assert.Equal("stage", await Read(source, staging));

        string fresh = root + "/published";
        Assert.Null(await SafeFileWriteService.WriteDocumentAsync(source, fresh, Encoding.UTF8.GetBytes("complete")));
        Assert.Equal("complete", await Read(source, fresh));
        Assert.Null(await SafeFileWriteService.WriteDocumentAsync(source, target, Encoding.UTF8.GetBytes("replacement")));
        Assert.Equal("replacement", await Read(source, target));
        await ExerciseCopiesAndRename(source, root);

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeFileWriteService.WriteProviderAsync(source, root + "/cancelled", null, 4, async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data"));
                cts.Cancel();
                return 4;
            }, false, cts.Token));
        Assert.Null(source.GetItem(root + "/cancelled"));
        Assert.DoesNotContain(source.EnumerateDirectory(root), entry => entry.Name.Contains(".csharpfar-"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ftp_BestEffortCopyOverwriteAndRename(bool tls)
    {
        if (!Enabled) return;
        using var source = new FtpFilePanelSource(new FtpConnectionInfo
        {
            Id = "publication-integration", DisplayName = "Loopback integration FTP",
            Host = "127.0.0.1", Port = int.Parse(Env(tls ? "CSHARPFAR_FTPS_PORT" : "CSHARPFAR_FTP_PORT")),
            Username = Env("CSHARPFAR_TEST_USER"),
            SecurityMode = tls ? FtpConnectionSecurityMode.ExplicitFtps : FtpConnectionSecurityMode.PlainFtp,
            UseDataConnectionTls = tls,
        }, Env("CSHARPFAR_TEST_PASSWORD"), acceptCertificate: (_, _) => true);

        Assert.Equal("original", await Read(source, "/target"));
        await ExerciseCopiesAndRename(source, "/");
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeFileWriteService.WriteProviderAsync(source, "/cancelled", null, 4, async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data"));
                cts.Cancel();
                return 4;
            }, false, cts.Token));
        await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await using var stream = await source.OpenWriteAsync("/target", overwrite: false);
        });
        Assert.Equal("original", await Read(source, "/target"));
        await Assert.ThrowsAnyAsync<IOException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/missing-directory/file", Encoding.UTF8.GetBytes("data")));
        await Assert.ThrowsAsync<UnknownFilePublicationException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/reject-completion", Encoding.UTF8.GetBytes("data")));
        // The server received the bytes but refused to confirm the transfer.
        Assert.Equal("data", await Read(source, "/reject-completion"));
    }

    private static async Task ExerciseCopiesAndRename(IFilePanelSource remote, string root)
    {
        string localRoot = Path.Combine(Path.GetTempPath(), "network-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localRoot);
        try
        {
            string local = Path.Combine(localRoot, "upload");
            await File.WriteAllTextAsync(local, "first");
            string target = remote.CombinePath(root, "upload");
            var service = new FileOperationService(new FilePanelSourceRegistry(
                [new LocalFilePanelSource(new FileSystemService()), remote]));
            async Task<FileOperationResult> Execute(FileOperationKind kind, PanelLocation from, PanelLocation to,
                ConflictDecisionMode decision = ConflictDecisionMode.Overwrite) =>
                await service.ExecuteAsync(new FileOperationRequest
                {
                    Kind = kind, Sources = [from], Destination = to,
                    Options = new FileOperationOptions { DefaultConflictDecision = decision },
                }, null, new Resolver());
            Assert.Equal(1, (await Execute(FileOperationKind.Copy, PanelLocation.Local(local),
                new PanelLocation(remote.SourceId, target))).CopiedCount);
            Assert.Equal("first", await Read(remote, target));
            await File.WriteAllTextAsync(local, "replacement contents");
            Assert.Equal(1, (await Execute(FileOperationKind.Copy, PanelLocation.Local(local),
                new PanelLocation(remote.SourceId, target), ConflictDecisionMode.Skip)).SkippedCount);
            Assert.Equal("first", await Read(remote, target));
            Assert.Equal(1, (await Execute(FileOperationKind.Copy, PanelLocation.Local(local),
                new PanelLocation(remote.SourceId, target))).CopiedCount);
            Assert.Equal("replacement contents", await Read(remote, target));
            string renamed = remote.CombinePath(root, "renamed-upload");
            Assert.Equal(1, (await Execute(FileOperationKind.Move,
                new PanelLocation(remote.SourceId, target), new PanelLocation(remote.SourceId, renamed))).MovedCount);
            Assert.Null(remote.GetItem(target));
            Assert.Equal("replacement contents", await Read(remote, renamed));
            string occupied = remote.CombinePath(root, "occupied");
            Assert.Null(await SafeFileWriteService.WriteDocumentAsync(remote, occupied, Encoding.UTF8.GetBytes("protected")));
            Assert.Equal(1, (await Execute(FileOperationKind.Move,
                new PanelLocation(remote.SourceId, renamed), new PanelLocation(remote.SourceId, occupied),
                ConflictDecisionMode.Skip)).SkippedCount);
            Assert.Equal("protected", await Read(remote, occupied));
            Assert.Equal("replacement contents", await Read(remote, renamed));
            string downloaded = Path.Combine(localRoot, "download");
            Assert.Equal(1, (await Execute(FileOperationKind.Copy, new PanelLocation(remote.SourceId, renamed),
                PanelLocation.Local(downloaded))).CopiedCount);
            Assert.Equal("replacement contents", await File.ReadAllTextAsync(downloaded));
        }
        finally { Directory.Delete(localRoot, recursive: true); }
    }

    private sealed class Resolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private static async Task<string> Read(CSharpFar.Core.Abstractions.IFilePanelSource source, string path)
    {
        await using var stream = await source.OpenReadAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
