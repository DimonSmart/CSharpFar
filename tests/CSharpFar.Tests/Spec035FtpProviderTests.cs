using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;
using FluentFTP;

using static CSharpFar.Tests.RemoteConnectionDialogTestFixture;

namespace CSharpFar.Tests;

public sealed partial class Spec035FtpProviderTests : IDisposable
{
    private readonly RemoteConnectionTestWorkspace _workspace = new("CSharpFar.Spec035.");
    private string _tempDir => _workspace.DirectoryPath;

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FtpConnectionDialog_TypingConnectionNameWithoutSaveConnectionRowDoesNotCrash()
    {
        var driver = CreateDriver();
        driver.EnqueueKey(Printable('n'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = CreateFtpDialog(ModalTestHost.Create(new ScreenRenderer(driver))).Show(
                new FtpConnectionDialogRequest(
                    Connection: TestConnection(),
                    SavedPassword: "secret-password",
                    SaveConnectionByDefault: true,
                    AllowTemporaryConnection: false),
                _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("testn", result.Connection.DisplayName);
    }

    [Fact]
    public void PanelSourceId_Module_UsesLegacyPluginPrefix()
    {
        Assert.Equal($"plugin:{FtpModuleIds.ModuleId:D}:main", PanelSourceId.Module(FtpModuleIds.ModuleId, "main").Value);
        Assert.Throws<ArgumentException>(() => PanelSourceId.Module(FtpModuleIds.ModuleId, " "));
    }

    [Fact]
    public void FtpConnectionStore_DoesNotWritePlaintextPasswordToMetadata()
    {
        Directory.CreateDirectory(_tempDir);
        var store = new FtpConnectionStore(_tempDir);

        store.Save(
        [
            TestConnection() with
            {
                CredentialId = "ftp-cred1",
                ExpectedTlsCertificateFingerprint = "AA:BB",
            },
        ]);

        string metadata = File.ReadAllText(Path.Combine(_tempDir, "ftp-connections.json"));
        Assert.DoesNotContain("secret-password", metadata, StringComparison.Ordinal);
        Assert.Contains("\"CredentialId\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"SecurityMode\": 1", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ShowInDriveSelection\"", metadata, StringComparison.Ordinal);
        Assert.Equal("ftp-cred1", store.Load().Single().CredentialId);
    }

    [Fact]
    public void FtpConnectionStore_LegacyDriveSettingIsIgnoredAndRemovedOnSave()
    {
        Directory.CreateDirectory(_tempDir);
        string path = Path.Combine(_tempDir, "ftp-connections.json");
        File.WriteAllText(path, """
            [
              {
                "Id": "legacy",
                "DisplayName": "Legacy FTP",
                "Host": "example.test",
                "Username": "user",
                "ShowInDriveSelection": false
              }
            ]
            """);
        var store = new FtpConnectionStore(_tempDir);

        var connections = store.Load();
        Assert.Single(connections);
        Assert.Equal("Legacy FTP", connections[0].DisplayName);
        store.Save(connections);
        Assert.DoesNotContain("\"ShowInDriveSelection\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FtpConnectionSecurityMode.PlainFtp, FtpEncryptionMode.None, false)]
    [InlineData(FtpConnectionSecurityMode.ExplicitFtps, FtpEncryptionMode.Explicit, true)]
    [InlineData(FtpConnectionSecurityMode.ImplicitFtps, FtpEncryptionMode.Implicit, true)]
    [InlineData(FtpConnectionSecurityMode.Auto, FtpEncryptionMode.Auto, true)]
    public void FtpConfig_MapsSecurityAndDataTls(
        FtpConnectionSecurityMode securityMode,
        FtpEncryptionMode expectedEncryptionMode,
        bool expectedDataTls)
    {
        var config = FtpFilePanelSource.CreateConfig(TestConnection() with
        {
            SecurityMode = securityMode,
            UseDataConnectionTls = true,
        });

        Assert.Equal(expectedEncryptionMode, config.EncryptionMode);
        Assert.Equal(expectedDataTls, config.DataConnectionEncryption);
        Assert.False(config.ValidateAnyCertificate);
    }

    [Theory]
    [InlineData(FtpDataConnectionMode.AutoPassive, FtpDataConnectionType.AutoPassive)]
    [InlineData(FtpDataConnectionMode.Passive, FtpDataConnectionType.PASV)]
    [InlineData(FtpDataConnectionMode.Active, FtpDataConnectionType.AutoActive)]
    public void FtpConfig_MapsDataConnectionMode(
        FtpDataConnectionMode dataConnectionMode,
        FtpDataConnectionType expectedConnectionType)
    {
        var config = FtpFilePanelSource.CreateConfig(TestConnection() with
        {
            DataConnectionMode = dataConnectionMode,
        });

        Assert.Equal(expectedConnectionType, config.DataConnectionType);
    }

    [Fact]
    public void FtpConfig_MapsActivePortRange()
    {
        var config = FtpFilePanelSource.CreateConfig(TestConnection() with
        {
            DataConnectionMode = FtpDataConnectionMode.Active,
            ActiveModeLocalPortFrom = 50000,
            ActiveModeLocalPortTo = 50002,
        });

        Assert.Equal([50000, 50001, 50002], config.ActivePorts.ToArray());
    }

    [Fact]
    public void FtpConfig_RejectsInvalidActivePortRange()
    {
        Assert.Throws<ArgumentException>(() => FtpFilePanelSource.CreateConfig(TestConnection() with
        {
            DataConnectionMode = FtpDataConnectionMode.Active,
            ActiveModeLocalPortFrom = 50002,
            ActiveModeLocalPortTo = 50000,
        }));
    }

    [Fact]
    public void FtpFilePanelSource_NormalizesProviderPaths()
    {
        var source = new FtpFilePanelSource(
            TestConnection() with { RemoteRootPath = "/home/user" },
            "secret-password");

        Assert.Equal("/home/user", source.NormalizePath(""));
        Assert.Equal("/var/log", source.NormalizePath("var\\./tmp/../log/"));
        Assert.Equal("/", source.NormalizePath("/../"));
        Assert.Equal("/var", source.GetParentPath("/var/log"));
        Assert.Null(source.GetParentPath("/"));
    }

    [Fact]
    public async Task FileOperationService_BlocksProviderToProviderCopy()
    {
        var service = new FileOperationService(new FilePanelSourceRegistry([]));

        var ex = await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [new PanelLocation(PanelSourceId.Module(FtpModuleIds.ModuleId, "left"), "/source.txt")],
                DestinationLocation = new PanelLocation(PanelSourceId.Module(FtpModuleIds.ModuleId, "right"), "/target"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new NoOpConflictResolver()));

        Assert.Contains("not available in the current composition", ex.Message, StringComparison.Ordinal);
    }

    private static FtpConnectionInfo TestConnection() =>
        new()
        {
            Id = "conn1",
            DisplayName = "test",
            Host = "example.test",
            Port = 21,
            Username = "user",
            RemoteRootPath = "/",
            SecurityMode = FtpConnectionSecurityMode.ExplicitFtps,
            DataConnectionMode = FtpDataConnectionMode.AutoPassive,
            UseDataConnectionTls = true,
        };

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=example.test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static void SeedAcceptedFtpHistory(FtpConnectionInfo connection, ITextFieldHistoryProvider historyRegistry)
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        _ = CreateFtpDialog(ModalTestHost.Create(screen), historyRegistry).Show(
            new FtpConnectionDialogRequest(connection, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());
    }









    private sealed class NoOpConflictResolver : CSharpFar.Core.Abstractions.IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }







}
