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
    public void FtpConnectionDialog_ShowsCursorInFocusedInputField()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        bool cursorWasVisibleBeforeInput = false;
        driver.BeforeReadInput = currentDriver =>
        {
            cursorWasVisibleBeforeInput = currentDriver.CursorVisible;
            Assert.True(currentDriver.CursorX > 0);
            Assert.True(currentDriver.CursorY > 0);
        };
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
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

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SavePassword);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Show in drive menu", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver(height: 12);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.True(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_DisablingSaveConnectionDisablesSavePassword()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
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
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save password"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", true, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

    [Fact]
    public void FtpConnectionDialog_EnablingSaveConnectionDoesNotEnableSavePassword()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        RunReadScript(driver,
            d => ClickRowByText(d, "Save connection"),
            d => d.EnqueueKey(Key(ConsoleKey.F10)));

        var result = CreateFtpDialog(ModalTestHost.Create(screen)).Show(
            new FtpConnectionDialogRequest(TestConnection(), "secret-password", SaveConnectionByDefault: false, AllowTemporaryConnection: true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

}
