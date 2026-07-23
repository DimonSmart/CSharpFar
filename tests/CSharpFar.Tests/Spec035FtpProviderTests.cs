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

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
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
            if (readCount == 1)
                ClickInputByLabel(currentDriver, "Host:");
            if (readCount == 2)
            {
                hostFieldFocused = IsCursorInsideInputForLabel(currentDriver, "Host:", out _);
                currentDriver.EnqueueKey(Key(ConsoleKey.Escape));
            }

            if (readCount < 2)
                currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
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
        RunReadScript(driver,
            d => ClickRowByText(d, "Show in drive menu"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
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

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
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

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
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
    public void FtpConnectionDialog_SavePasswordChangeEnablesConnectionOnly()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.True(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_DisablingSaveConnectionDisablesSavePassword()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.SaveConnection);
        Assert.False(result.SavePassword);
        Assert.Null(result.Connection.CredentialId);
    }

    [Fact]
    public void FtpConnectionDialog_DisablingSavePasswordKeepsSaveConnection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_EnablingSaveConnectionDoesNotEnableSavePassword()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", SaveConnectionByDefault: false, AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_SecurityChangeUpdatesDefaultPort()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ImplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(990, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_LeftSecurityChangeUsesActualPreviousMode()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.LeftArrow));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 21 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(21, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_ClickSecurityChangePreservesCustomPort()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 2121 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ImplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(2121, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_LeftSecurityChangePreservesCustomPort()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.LeftArrow));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 2121 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(2121, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_PlainToAutoThroughLeftEnablesDataTls()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                SecurityMode = FtpConnectionSecurityMode.PlainFtp,
                Port = 21,
                UseDataConnectionTls = false,
                ExpectedTlsCertificateFingerprint = null,
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.True(result.Connection.UseDataConnectionTls);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Fact]
    public void FtpConnectionDialog_PlainFtpKeepsTlsAndTrustRowsVisibleDisabledAndIgnored()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        bool dataTlsVisible = false;
        bool trustVisible = false;
        bool clickDidNotFocusDataTls = false;
        RunReadScript(driver,
            d =>
            {
                dataTlsVisible = HasRenderedText(d, "Use TLS for data connection");
                trustVisible = HasRenderedText(d, "Trust certificate");
                ClickRowByText(d, "Use TLS for data connection");
            },
            d =>
            {
                clickDidNotFocusDataTls = !IsCursorOnRowText(d, "Use TLS for data connection");
                ClickRowByText(d, "Trust certificate");
            },
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.Spacebar));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                SecurityMode = FtpConnectionSecurityMode.PlainFtp,
                UseDataConnectionTls = true,
                ExpectedTlsCertificateFingerprint = "AA:BB:CC",
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(dataTlsVisible);
        Assert.True(trustVisible);
        Assert.True(clickDidNotFocusDataTls);
        Assert.False(result.Connection.UseDataConnectionTls);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Fact]
    public void FtpConnectionDialog_SecureToPlainClearsTlsAndCertificateTrust()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                SecurityMode = FtpConnectionSecurityMode.Auto,
                Port = 21,
                ExpectedTlsCertificateFingerprint = "AA:BB",
                UseDataConnectionTls = true,
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.PlainFtp, result.Connection.SecurityMode);
        Assert.False(result.Connection.UseDataConnectionTls);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Fact]
    public void FtpConnectionDialog_CompactLabelsRenderExactlyOneColon()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Security: Explicit FTPS", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Data mode: Auto passive", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("Security::", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("Data mode::", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, 'o', "Connect")]
    [InlineData(false, 's', "Save")]
    public void FtpConnectionDialog_SubmitHotkeyMatchesSubmitLabel(bool temporary, char hotkey, string submitLabel)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(CharKey(hotkey));

        int validationCalls = 0;
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, temporary),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(1, validationCalls);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains(submitLabel, StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_CancelHotkeyDoesNotValidate()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(CharKey('c'));

        int validationCalls = 0;
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.Null(result);
        Assert.Equal(0, validationCalls);
    }

    [Fact]
    public void FtpConnectionDialog_EnterInTextInputSubmitsForm()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        int validationCalls = 0;

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(1, validationCalls);
    }

    [Fact]
    public void FtpConnectionDialog_HistoryEnterSelectsItemBeforeSubmit()
    {
        string host = "history-enter-" + Guid.NewGuid().ToString("N") + ".test";
        SeedAcceptedFtpHistory(TestConnection() with { Host = host });
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Host:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, host[..8]);
                d.EnqueueKey(Key(ConsoleKey.Enter));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });
        int validationCalls = 0;

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(1, validationCalls);
        Assert.Equal(host, result.Connection.Host);
    }

    [Fact]
    public void FtpConnectionDialog_HistoryEscapeClosesOverlayBeforeDialogCancel()
    {
        string host = "history-escape-" + Guid.NewGuid().ToString("N") + ".test";
        SeedAcceptedFtpHistory(TestConnection() with { Host = host });
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Host:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, host[..8]);
                d.EnqueueKey(Key(ConsoleKey.Escape));
                d.EnqueueKey(Key(ConsoleKey.Escape));
            });
        int validationCalls = 0;

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.Null(result);
        Assert.Equal(0, validationCalls);
    }

    [Fact]
    public void FtpConnectionDialog_ChoiceRowEnterChangesValueWithoutSubmitting()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.Enter));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });
        int validationCalls = 0;

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(1, validationCalls);
        Assert.Equal(FtpConnectionSecurityMode.Auto, result.Connection.SecurityMode);
    }

    [Fact]
    public void FtpConnectionDialog_CertificateReviewPreservesStateAndTrustsOnNextSubmit()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var candidates = new List<FtpConnectionDialogResult>();
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                DisplayName = "certificate-review",
                CredentialId = "ftp-review-credential",
                Host = "ftp.example.test",
                Port = 2121,
                Username = "review-user",
                RemoteRootPath = "/remote/root",
                DataConnectionMode = FtpDataConnectionMode.Active,
                UseDataConnectionTls = true,
                ActiveModeLocalPortFrom = 50000,
                ActiveModeLocalPortTo = 50010,
            }, "review-password", true, true),
            candidate =>
            {
                candidates.Add(candidate);
                return candidates.Count <= 2
                    ? FtpConnectionDialogValidationResult.RequireCertificateTrust("AA:BB:CC")
                    : FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(3, candidates.Count);
        Assert.Null(candidates[0].Connection.ExpectedTlsCertificateFingerprint);
        Assert.Null(candidates[1].Connection.ExpectedTlsCertificateFingerprint);
        Assert.Equal("AA:BB:CC", candidates[2].Connection.ExpectedTlsCertificateFingerprint);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("AA:BB:CC", StringComparison.Ordinal));
        Assert.Equal("certificate-review", result.Connection.DisplayName);
        Assert.Equal("ftp.example.test", result.Connection.Host);
        Assert.Equal(2121, result.Connection.Port);
        Assert.Equal("review-user", result.Connection.Username);
        Assert.Equal("review-password", result.Password);
        Assert.Equal("/remote/root", result.Connection.RemoteRootPath);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(FtpDataConnectionMode.Active, result.Connection.DataConnectionMode);
        Assert.True(result.Connection.UseDataConnectionTls);
        Assert.Equal("AA:BB:CC", result.Connection.ExpectedTlsCertificateFingerprint);
        Assert.Equal(50000, result.Connection.ActiveModeLocalPortFrom);
        Assert.Equal(50010, result.Connection.ActiveModeLocalPortTo);
        Assert.True(result.SaveConnection);
        Assert.True(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_EndpointChangeClearsCertificateTrust()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => d.EnqueueKey(Key(ConsoleKey.F10)),
            d => ClickRowByText(d, "Trust certificate"),
            d => ClickInputByLabel(d, "Host:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, "changed.example.test");
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var candidates = new List<FtpConnectionDialogResult>();
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { ExpectedTlsCertificateFingerprint = null }, "secret-password", true, true),
            candidate =>
            {
                candidates.Add(candidate);
                return candidates.Count == 1
                    ? FtpConnectionDialogValidationResult.RequireCertificateTrust("AA:BB:CC")
                    : FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(2, candidates.Count);
        Assert.Equal("changed.example.test", candidates[1].Connection.Host);
        Assert.Null(candidates[1].Connection.ExpectedTlsCertificateFingerprint);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Fact]
    public void FtpConnectionDialog_PortChangeClearsCertificateTrust()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => d.EnqueueKey(Key(ConsoleKey.F10)),
            d => ClickRowByText(d, "Trust certificate"),
            d => ClickInputByLabel(d, "Port:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, "2122");
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var candidates = new List<FtpConnectionDialogResult>();
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { ExpectedTlsCertificateFingerprint = null }, "secret-password", true, true),
            candidate =>
            {
                candidates.Add(candidate);
                return candidates.Count == 1
                    ? FtpConnectionDialogValidationResult.RequireCertificateTrust("AA:BB:CC")
                    : FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(2122, candidates[1].Connection.Port);
        Assert.Null(candidates[1].Connection.ExpectedTlsCertificateFingerprint);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Theory]
    [InlineData(FtpConnectionSecurityMode.ExplicitFtps, FtpConnectionSecurityMode.ImplicitFtps)]
    [InlineData(FtpConnectionSecurityMode.ImplicitFtps, FtpConnectionSecurityMode.Auto)]
    public void FtpConnectionDialog_SecurityChangeClearsCertificateTrust(
        FtpConnectionSecurityMode initialMode,
        FtpConnectionSecurityMode expectedMode)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                SecurityMode = initialMode,
                Port = initialMode == FtpConnectionSecurityMode.ImplicitFtps ? 990 : 21,
                ExpectedTlsCertificateFingerprint = "AA:BB:CC",
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(expectedMode, result.Connection.SecurityMode);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Fact]
    public void FtpConnectionDialog_AutoToSecureSecurityChangeClearsCertificateTrust()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                SecurityMode = FtpConnectionSecurityMode.Auto,
                ExpectedTlsCertificateFingerprint = "AA:BB:CC",
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Null(result.Connection.ExpectedTlsCertificateFingerprint);
    }

    [Theory]
    [InlineData("not-a-range")]
    [InlineData("0-10")]
    [InlineData("10-65536")]
    [InlineData("20-10")]
    public void FtpConnectionDialog_InvalidActivePortRangeKeepsDialogOpen(string invalidRange)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Active ports:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, invalidRange);
                d.EnqueueKey(Key(ConsoleKey.F10));
                d.EnqueueKey(Key(ConsoleKey.Escape));
            });

        int validationCalls = 0;
        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { DataConnectionMode = FtpDataConnectionMode.Active }, "secret-password", true, true),
            _ =>
            {
                validationCalls++;
                return FtpConnectionDialogValidationResult.Accepted();
            });

        Assert.Null(result);
        Assert.Equal(0, validationCalls);
    }

    [Fact]
    public void FtpConnectionDialog_ActivePortsBufferSurvivesConditionalRowChanges()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Active ports:"),
            d => EnqueueText(d, "50000-50010"),
            d => ClickRowByText(d, "Data mode:"),
            d => ClickRowByText(d, "Data mode:"),
            d => ClickRowByText(d, "Data mode:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { DataConnectionMode = FtpDataConnectionMode.Active }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpDataConnectionMode.Active, result.Connection.DataConnectionMode);
        Assert.Equal(50000, result.Connection.ActiveModeLocalPortFrom);
        Assert.Equal(50010, result.Connection.ActiveModeLocalPortTo);
    }

    [Theory]
    [InlineData("50000", 50000, 50000)]
    [InlineData("", null, null)]
    public void FtpConnectionDialog_ActivePortsAcceptsSinglePortAndEmptyValue(string text, int? expectedFrom, int? expectedTo)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Active ports:"),
            d =>
            {
                if (text.Length > 0)
                    EnqueueText(d, text);
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { DataConnectionMode = FtpDataConnectionMode.Active }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpDataConnectionMode.Active, result.Connection.DataConnectionMode);
        Assert.Equal(expectedFrom, result.Connection.ActiveModeLocalPortFrom);
        Assert.Equal(expectedTo, result.Connection.ActiveModeLocalPortTo);
    }

    [Theory]
    [InlineData(FtpDataConnectionMode.AutoPassive)]
    [InlineData(FtpDataConnectionMode.Passive)]
    public void FtpConnectionDialog_HiddenActivePortsDoNotEnterResult(FtpDataConnectionMode visibleMode)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with
            {
                DataConnectionMode = visibleMode,
                ActiveModeLocalPortFrom = 50000,
                ActiveModeLocalPortTo = 50010,
            }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(visibleMode, result.Connection.DataConnectionMode);
        Assert.Null(result.Connection.ActiveModeLocalPortFrom);
        Assert.Null(result.Connection.ActiveModeLocalPortTo);
    }

    [Fact]
    public void FtpConnectionDialog_MandatorySaveAlwaysReturnsSaveConnection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", false, false),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.Equal("secret-password", result.Password);
    }

    [Fact]
    public void FtpConnectionDialog_PasswordIsMaskedAndNeverAddedToHistory()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "unique-secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("unique-secret-password", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("secret-password", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_RejectedValidationDoesNotAddHistory()
    {
        string rejectedHost = "rejected-history-" + Guid.NewGuid().ToString("N") + ".test";
        var rejectedDriver = new FakeConsoleDriver(width: 100, height: 30);
        var rejectedScreen = new ScreenRenderer(rejectedDriver);
        rejectedDriver.EnqueueKey(Key(ConsoleKey.F10));
        rejectedDriver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = new FtpConnectionDialog(ModalTestHost.Create(rejectedScreen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { Host = rejectedHost }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Error("rejected"));

        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Host:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, rejectedHost[..8]);
                d.EnqueueKey(Key(ConsoleKey.Escape));
            });

        _ = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains(rejectedHost, StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_ResizePreservesFocusedRowBufferAndUsesCommittedFrameForMouse()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 16);
        var screen = new ScreenRenderer(driver);
        bool cursorWasInHostInputAfterResize = false;
        string cursorDiagnostic = string.Empty;
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Host:"),
            d => d.EnqueueKey(ControlA()),
            d => d.EnqueueKey(Printable('r')),
            d => d.EnqueueKey(Printable('e')),
            d => d.EnqueueKey(Printable('s')),
            d => d.EnqueueKey(Printable('i')),
            d => d.EnqueueKey(Printable('z')),
            d => d.EnqueueKey(Printable('e')),
            d => d.EnqueueKey(Printable('d')),
            d =>
            {
                d.SetSize(86, 24);
                d.EnqueueInput(new ConsoleResizeInputEvent());
            },
            d =>
            {
                cursorWasInHostInputAfterResize = IsCursorInsideInputForLabel(d, "Host:", out cursorDiagnostic);
                d.EnqueueKey(Printable('-'));
            },
            d => d.EnqueueKey(Printable('h')),
            d => d.EnqueueKey(Printable('o')),
            d => d.EnqueueKey(Printable('s')),
            d => d.EnqueueKey(Printable('t')),
            d => ClickRowByText(d, "Show in drive menu"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("resized-host", result.Connection.Host);
        Assert.False(result.Connection.ShowInDriveSelection);
        Assert.True(cursorWasInHostInputAfterResize, cursorDiagnostic);
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
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
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

    private static void SeedAcceptedFtpHistory(FtpConnectionInfo connection)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        _ = new FtpConnectionDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(connection, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());
    }

    private static void RunReadScript(FakeConsoleDriver driver, params Action<FakeConsoleDriver>[] steps)
    {
        int index = 0;
        Action<FakeConsoleDriver>? callback = null;
        callback = currentDriver =>
        {
            if (index >= steps.Length)
                return;

            steps[index++](currentDriver);
            if (index < steps.Length)
                currentDriver.BeforeReadInput = callback;
        };
        driver.BeforeReadInput = callback;
    }

    private static void ClickInputByLabel(FakeConsoleDriver driver, string label)
    {
        var record = LastRenderedText(driver, label);
        driver.EnqueueInput(LeftMouse(record.X + 22, record.Y));
    }

    private static void ClickRowByText(FakeConsoleDriver driver, string text)
    {
        var record = LastRenderedText(driver, text);
        driver.EnqueueInput(LeftMouse(record.X + Math.Min(1, Math.Max(0, record.Text.Length - 1)), record.Y));
    }

    private static bool HasRenderedText(FakeConsoleDriver driver, string text) =>
        driver.WriteRecords.Any(record => record.Text.Contains(text, StringComparison.Ordinal));

    private static bool IsCursorInsideInputForLabel(FakeConsoleDriver driver, string label, out string diagnostic)
    {
        var labelRecord = LastRenderedText(driver, label);
        int inputX = labelRecord.X + 22;
        int inputWidth = 44;
        diagnostic = $"cursor=({driver.CursorX},{driver.CursorY}) visible={driver.CursorVisible}; label=({labelRecord.X},{labelRecord.Y}) input=[{inputX},{inputX + inputWidth})";
        return driver.CursorVisible &&
            driver.CursorY == labelRecord.Y &&
            driver.CursorX >= inputX &&
            driver.CursorX < inputX + inputWidth;
    }

    private static bool IsCursorOnRowText(FakeConsoleDriver driver, string text)
    {
        var record = LastRenderedText(driver, text);
        return driver.CursorVisible && driver.CursorY == record.Y;
    }

    private static FakeConsoleDriver.WriteRecord LastRenderedText(FakeConsoleDriver driver, string text) =>
        driver.WriteRecords.Last(record => record.Text.Contains(text, StringComparison.Ordinal));

    private sealed class NoOpConflictResolver : CSharpFar.Core.Abstractions.IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo ControlA() =>
        new('\u0001', ConsoleKey.A, shift: false, alt: false, control: true);

    private static ConsoleKeyInfo Printable(char character) =>
        new(character, ConsoleKey.NoName, shift: false, alt: false, control: false);

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char character in text)
            driver.EnqueueKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
    }

    private static ConsoleKeyInfo ShiftTab() =>
        new('\0', ConsoleKey.Tab, shift: true, alt: false, control: false);

    private static ConsoleKeyInfo CharKey(char key) =>
        new(key, Enum.Parse<ConsoleKey>(key.ToString(), ignoreCase: true), shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
