using System.Text;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

using static CSharpFar.Tests.RemoteConnectionDialogTestFixture;

namespace CSharpFar.Tests;

public sealed partial class Spec029SftpProviderTests
{
    [Fact]
    public void SftpConnectionStore_DoesNotWritePlaintextPasswordToMetadata()
    {
        Directory.CreateDirectory(_tempDir);
        var store = new SftpConnectionStore(_tempDir);

        store.Save(
        [
            new SftpConnectionInfo
            {
                Id = "conn1",
                DisplayName = "test",
                Host = "example.test",
                Port = 22,
                Username = "user",
                RemoteRootPath = "/",
                CredentialId = "cred1",
                ExpectedHostKeyFingerprint = "AA:BB",
            },
        ]);

        string metadata = File.ReadAllText(Path.Combine(_tempDir, "connections.json"));
        Assert.DoesNotContain("secret-password", metadata, StringComparison.Ordinal);
        Assert.Contains("\"CredentialId\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ShowInDriveSelection\"", metadata, StringComparison.Ordinal);
        Assert.Equal("cred1", store.Load().Single().CredentialId);
    }

    [Fact]
    public void SftpConnectionStore_LegacyDriveSettingIsIgnoredAndRemovedOnSave()
    {
        Directory.CreateDirectory(_tempDir);
        string path = Path.Combine(_tempDir, "connections.json");
        File.WriteAllText(path, """
            [
              {
                "Id": "legacy",
                "DisplayName": "Legacy SFTP",
                "Host": "example.test",
                "Username": "user",
                "ShowInDriveSelection": false
              }
            ]
            """);
        var store = new SftpConnectionStore(_tempDir);

        var connections = store.Load();
        Assert.Single(connections);
        Assert.Equal("Legacy SFTP", connections[0].DisplayName);
        store.Save(connections);
        Assert.DoesNotContain("\"ShowInDriveSelection\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

}
