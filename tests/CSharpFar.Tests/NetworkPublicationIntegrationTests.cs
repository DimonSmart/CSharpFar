using System.Text;
using CSharpFar.Core.Models;
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
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, target, Encoding.UTF8.GetBytes("replacement")));
        Assert.Equal("original", await Read(source, target));

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

    [Fact]
    public async Task Ftp_RefusesUnverifiedPublicationAndPreservesExistingFile()
    {
        if (!Enabled) return;
        using var source = new FtpFilePanelSource(new FtpConnectionInfo
        {
            Id = "publication-integration", DisplayName = "Loopback integration FTP",
            Host = "127.0.0.1", Port = int.Parse(Env("CSHARPFAR_FTP_PORT")),
            Username = Env("CSHARPFAR_TEST_USER"), SecurityMode = FtpConnectionSecurityMode.PlainFtp,
            UseDataConnectionTls = false,
        }, Env("CSHARPFAR_TEST_PASSWORD"));

        Assert.Equal("original", await Read(source, "/target"));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("replacement")));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/new", Encoding.UTF8.GetBytes("new")));
        Assert.Equal("original", await Read(source, "/target"));
        Assert.Null(source.GetItem("/new"));
    }

    private static async Task<string> Read(CSharpFar.Core.Abstractions.IFilePanelSource source, string path)
    {
        await using var stream = await source.OpenReadAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
