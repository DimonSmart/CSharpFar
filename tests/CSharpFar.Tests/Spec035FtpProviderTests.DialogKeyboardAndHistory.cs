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
    public void FtpConnectionDialog_CompactLabelsRenderExactlyOneColon()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(CharKey(hotkey));

        int validationCalls = 0;
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(CharKey('c'));

        int validationCalls = 0;
        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        int validationCalls = 0;

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        ITextFieldHistoryProvider historyRegistry = TextFieldHistoryTestProvider.Create();
        SeedAcceptedFtpHistory(TestConnection() with { Host = host }, historyRegistry);
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickInputByLabel(d, "Host:"),
            d =>
            {
                d.EnqueueKey(ControlA());
                EnqueueText(d, host[..8]);
                d.EnqueueKey(Key(ConsoleKey.DownArrow));
                d.EnqueueKey(Key(ConsoleKey.Enter));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });
        int validationCalls = 0;

        var result = CreateFtpDialog(ModalTestHost.Create(screen), historyRegistry).Show(
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
        ITextFieldHistoryProvider historyRegistry = TextFieldHistoryTestProvider.Create();
        SeedAcceptedFtpHistory(TestConnection() with { Host = host }, historyRegistry);
        var driver = CreateDriver();
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

        var result = CreateFtpDialog(ModalTestHost.Create(screen), historyRegistry).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Security:"),
            d =>
            {
                d.EnqueueKey(Key(ConsoleKey.Enter));
                d.EnqueueKey(Key(ConsoleKey.F10));
            });
        int validationCalls = 0;

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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

}
