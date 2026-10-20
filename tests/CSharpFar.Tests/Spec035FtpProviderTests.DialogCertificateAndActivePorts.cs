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

public sealed partial class Spec035FtpProviderTests
{
    [Fact]
    public void FtpConnectionDialog_CertificateReviewPreservesStateAndTrustsOnNextSubmit()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var candidates = new List<FtpConnectionDialogResult>();
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
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
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
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
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
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
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Active ports:"),
            d => EnqueueText(d, "50000-50010"),
            d => ClickRowByText(d, "Data mode:"),
            d => ClickRowByText(d, "Data mode:"),
            d => ClickRowByText(d, "Data mode:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Active ports:"),
            d =>
            {
                if (text.Length > 0)
                    EnqueueText(d, text);
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", false, false),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.Equal("secret-password", result.Password);
    }

}
