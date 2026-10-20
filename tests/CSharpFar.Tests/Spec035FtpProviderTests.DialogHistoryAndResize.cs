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
    public void FtpConnectionDialog_PasswordIsMaskedAndNeverAddedToHistory()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "unique-secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("unique-secret-password", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("secret-password", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_AcceptedValidationAddsOnlyNonSecretHistory()
    {
        string host = "accepted-history-" + Guid.NewGuid().ToString("N") + ".test";
        const string password = "ftp-history-secret";
        ITextFieldHistoryProvider history = TextFieldHistoryTestProvider.Create();
        var driver = CreateDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));

        _ = CreateFtpDialog(ModalTestHost.Create(new ScreenRenderer(driver)), history).Show(
            new FtpConnectionDialogRequest(TestConnection() with { Host = host }, password, true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Contains(host, history.Get(FtpTextHistoryIds.Host).Items);
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains(password, StringComparison.Ordinal));
        Assert.All(
            new[] { FtpTextHistoryIds.ConnectionName, FtpTextHistoryIds.Host, FtpTextHistoryIds.Port, FtpTextHistoryIds.UserName, FtpTextHistoryIds.RemoteRoot, FtpTextHistoryIds.ActivePorts },
            id => Assert.DoesNotContain(history.Get(id).Items, item => item.Contains(password, StringComparison.Ordinal)));
    }

    [Fact]
    public void FtpConnectionDialog_RejectedValidationAndCancelDoNotAddHistory()
    {
        string rejectedHost = "rejected-history-" + Guid.NewGuid().ToString("N") + ".test";
        ITextFieldHistoryProvider history = TextFieldHistoryTestProvider.Create();
        var rejectedDriver = CreateDriver();
        rejectedDriver.EnqueueKey(Key(ConsoleKey.F10));
        rejectedDriver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = CreateFtpDialog(ModalTestHost.Create(new ScreenRenderer(rejectedDriver)), history).Show(
            new FtpConnectionDialogRequest(TestConnection() with { Host = rejectedHost }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Error("rejected"));

        var cancelDriver = CreateDriver();
        cancelDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = CreateFtpDialog(ModalTestHost.Create(new ScreenRenderer(cancelDriver)), history).Show(
            new FtpConnectionDialogRequest(TestConnection() with { Host = "cancelled-history.test" }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.Empty(history.Get(FtpTextHistoryIds.Host).Items);
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
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("resized-host", result.Connection.Host);
        Assert.True(result.SavePassword);
        Assert.True(cursorWasInHostInputAfterResize, cursorDiagnostic);
    }

}
