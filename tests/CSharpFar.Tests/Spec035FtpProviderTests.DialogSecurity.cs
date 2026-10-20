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
    public void FtpConnectionDialog_SecurityChangeUpdatesDefaultPort()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ImplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(990, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_LeftSecurityChangeUsesActualPreviousMode()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.LeftArrow));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 21 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(21, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_ClickSecurityChangePreservesCustomPort()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 2121 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ImplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(2121, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_LeftSecurityChangePreservesCustomPort()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.LeftArrow));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { SecurityMode = FtpConnectionSecurityMode.ExplicitFtps, Port = 2121 }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionSecurityMode.ExplicitFtps, result.Connection.SecurityMode);
        Assert.Equal(2121, result.Connection.Port);
    }

    [Fact]
    public void FtpConnectionDialog_PlainToAutoThroughLeftEnablesDataTls()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
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

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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

}
