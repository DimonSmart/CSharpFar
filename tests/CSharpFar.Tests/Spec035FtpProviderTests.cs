using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;
using FluentFTP;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CSharpFar.Tests;

public sealed class Spec035FtpProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "CSharpFar.Spec035." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
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
                ShowInDriveSelection = false,
            },
        ]);

        string metadata = File.ReadAllText(Path.Combine(_tempDir, "ftp-connections.json"));
        Assert.DoesNotContain("secret-password", metadata, StringComparison.Ordinal);
        Assert.Contains("\"CredentialId\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"SecurityMode\": 1", metadata, StringComparison.Ordinal);
        Assert.Contains("\"ShowInDriveSelection\": false", metadata, StringComparison.Ordinal);
        Assert.False(store.Load().Single().ShowInDriveSelection);
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
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

        Assert.Contains("Provider-to-provider copy is not supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FtpConnectionDialog_ShowsCursorInFocusedInputField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        bool cursorWasVisibleBeforeInput = false;
        driver.BeforeReadInput = currentDriver =>
        {
            cursorWasVisibleBeforeInput = currentDriver.CursorVisible;
            Assert.True(currentDriver.CursorX > 0);
            Assert.True(currentDriver.CursorY > 0);
        };
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FtpConnectionDialog(screen).Show(
            new FtpConnectionDialogRequest(
                Connection: null,
                SavedPassword: null,
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(cursorWasVisibleBeforeInput);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("FTP/FTPS connection", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Connection name:", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_MouseClickFocusesTextField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        int readCount = 0;
        bool hostFieldFocused = false;
        Action<FakeConsoleDriver>? beforeRead = null;
        beforeRead = currentDriver =>
        {
            readCount++;
            if (readCount == 2)
            {
                hostFieldFocused =
                    currentDriver.CursorVisible &&
                    currentDriver.CursorX == 40 &&
                    currentDriver.CursorY == 7;
            }

            if (readCount < 2)
                currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;
        driver.EnqueueInput(LeftMouse(40, 7));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FtpConnectionDialog(screen).Show(
            new FtpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(hostFieldFocused);
    }

    [Fact]
    public void FtpConnectionDialog_MouseClickTogglesCheckboxField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(LeftMouse(14, 14));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FtpConnectionDialog(screen).Show(
            new FtpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.Connection.ShowInDriveSelection);
    }

    [Fact]
    public void FtpConnectionDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FtpConnectionDialog(screen).Show(
            new FtpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("test", result.Connection.DisplayName);
    }

    [Fact]
    public void FtpConnectionDialog_ShortConsoleRendersBodyScrollbar()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FtpConnectionDialog(screen).Show(
            new FtpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, r => r.Text == "▲");
        Assert.Contains(driver.WriteRecords, r => r.Text == "▼");
    }

    [Fact]
    public void FtpConnectionManagerDialog_NewButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var button = currentDriver.WriteRecords.Last(r => r.Text.Contains("New", StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        };

        var result = new FtpConnectionManagerDialog(ModalTestHost.Create(screen)).Show([]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Create, result.Action);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Connect", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Edit", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionManagerDialog_ButtonFocusSupportsKeyboardSelection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FtpConnectionManagerDialog(ModalTestHost.Create(screen)).Show([TestConnection()]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Create, result.Action);
    }

    [Fact]
    public void FtpConnectionManagerDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FtpConnectionManagerDialog(ModalTestHost.Create(screen)).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
    }

    [Fact]
    public void FtpsCertificateTrust_FirstAcceptPersistsFingerprint()
    {
        using var certificate = CreateCertificate();
        string? acceptedFingerprint = null;

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = null },
            certificate,
            acceptCertificate: (_, _) => true,
            acceptedCertificate: (_, fingerprint) => acceptedFingerprint = fingerprint,
            out string? errorMessage);

        Assert.True(accepted);
        Assert.Null(errorMessage);
        Assert.Equal(FtpFilePanelSource.FormatCertificateFingerprint(certificate), acceptedFingerprint);
    }

    [Fact]
    public void FtpsCertificateTrust_MatchingFingerprintReconnects()
    {
        using var certificate = CreateCertificate();
        string fingerprint = FtpFilePanelSource.FormatCertificateFingerprint(certificate);

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = fingerprint },
            certificate,
            acceptCertificate: (_, _) => false,
            acceptedCertificate: null,
            out string? errorMessage);

        Assert.True(accepted);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void FtpsCertificateTrust_ChangedFingerprintIsRejected()
    {
        using var certificate = CreateCertificate();

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = "AA:BB" },
            certificate,
            acceptCertificate: (_, _) => true,
            acceptedCertificate: null,
            out string? errorMessage);

        Assert.False(accepted);
        Assert.Contains("fingerprint changed", errorMessage, StringComparison.OrdinalIgnoreCase);
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

    private sealed class NoOpConflictResolver : CSharpFar.Core.Abstractions.IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo ShiftTab() =>
        new('\0', ConsoleKey.Tab, shift: true, alt: false, control: false);

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
