using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class TemporaryConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SftpManager_QuickConnectAvailableWithOrWithoutSavedConnections(bool hasSaved)
    {
        var driver = CreateQuickConnectClickDriver();
        var items = hasSaved ? new[] { SftpConnection() } : [];
        var result = new SftpConnectionManagerDialog(Dialogs(driver)).Show(items);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.QuickConnect, result.Action);
        Assert.Null(result.Connection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FtpManager_QuickConnectAvailableWithOrWithoutSavedConnections(bool hasSaved)
    {
        var driver = CreateQuickConnectClickDriver();
        var items = hasSaved ? new[] { FtpConnection() } : [];
        var result = new FtpConnectionManagerDialog(Dialogs(driver)).Show(items);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.QuickConnect, result.Action);
        Assert.Null(result.Connection);
    }

    [Fact]
    public void SftpQuickConnect_DoesNotPersistHistoryOrExposeDriveSetting()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var history = TextFieldHistoryTestProvider.Create();
        var fields = new FormFieldFactory(history);

        var result = new SftpConnectionDialog(
            new DialogService(ModalTestHost.Create(new ScreenRenderer(driver)), fields), fields).Show(
            new SftpConnectionDialogRequest(SftpConnection(), "secret", false, true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.SaveConnection);
        Assert.False(result.SavePassword);
        Assert.Empty(history.Get(SftpTextHistoryIds.Host).Items);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Show in drive menu", StringComparison.Ordinal));
        Assert.True(new SftpFilePanelSource(result.Connection, result.Password, isTemporary: true).IsTemporary);
    }

    [Fact]
    public void FtpQuickConnect_DoesNotPersistHistoryOrExposeDriveSetting()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var history = TextFieldHistoryTestProvider.Create();
        var fields = new FormFieldFactory(history);

        var result = new FtpConnectionDialog(
            new DialogService(ModalTestHost.Create(new ScreenRenderer(driver)), fields), fields).Show(
            new FtpConnectionDialogRequest(FtpConnection(), "secret", false, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.SaveConnection);
        Assert.False(result.SavePassword);
        Assert.Empty(history.Get(FtpTextHistoryIds.Host).Items);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Show in drive menu", StringComparison.Ordinal));
        Assert.True(new FtpFilePanelSource(result.Connection, result.Password, isTemporary: true).IsTemporary);
    }

    [Fact]
    public void SftpQuickConnect_ExplicitSaveKeepsExistingHistorySemantics()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        ClickThenSubmit(driver, "Save connection");
        var history = TextFieldHistoryTestProvider.Create();
        var fields = new FormFieldFactory(history);
        var result = new SftpConnectionDialog(
            new DialogService(ModalTestHost.Create(new ScreenRenderer(driver)), fields), fields).Show(
            new SftpConnectionDialogRequest(SftpConnection(), "secret", false, true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.Contains("sftp.test", history.Get(SftpTextHistoryIds.Host).Items);
    }

    [Fact]
    public void FtpQuickConnect_ExplicitSaveKeepsExistingHistorySemantics()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        ClickThenSubmit(driver, "Save connection");
        var history = TextFieldHistoryTestProvider.Create();
        var fields = new FormFieldFactory(history);
        var result = new FtpConnectionDialog(
            new DialogService(ModalTestHost.Create(new ScreenRenderer(driver)), fields), fields).Show(
            new FtpConnectionDialogRequest(FtpConnection(), "secret", false, true),
            _ => FtpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.Contains("ftp.test", history.Get(FtpTextHistoryIds.Host).Items);
    }

    private static FakeConsoleDriver CreateQuickConnectClickDriver()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.BeforeReadInput = d => Click(d, "Quick connect...");
        return driver;
    }

    private static void ClickThenSubmit(FakeConsoleDriver driver, string label)
    {
        int step = 0;
        Action<FakeConsoleDriver>? beforeRead = null;
        beforeRead = d =>
        {
            if (step++ == 0)
            {
                Click(d, label);
                d.BeforeReadInput = beforeRead;
            }
            else
                d.EnqueueKey(Key(ConsoleKey.F10));
        };
        driver.BeforeReadInput = beforeRead;
    }

    private static void Click(FakeConsoleDriver driver, string label)
    {
        var text = driver.WriteRecords.Last(r => r.Text.Contains(label, StringComparison.Ordinal));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            text.X + text.Text.IndexOf(label, StringComparison.Ordinal) + 1, text.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(
            text.X + text.Text.IndexOf(label, StringComparison.Ordinal) + 1, text.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
    }

    private static DialogService Dialogs(FakeConsoleDriver driver) =>
        new(ModalTestHost.Create(new ScreenRenderer(driver)),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    private static SftpConnectionInfo SftpConnection() => new()
    {
        Id = "sample", DisplayName = "SFTP", Host = "sftp.test",
        Username = "user", RemoteRootPath = "/"
    };

    private static FtpConnectionInfo FtpConnection() => new()
    {
        Id = "sample", DisplayName = "FTP", Host = "ftp.test",
        Username = "user", RemoteRootPath = "/"
    };

    private static ConsoleKeyInfo Key(ConsoleKey key) => new(
        '\0', key, shift: false, alt: false, control: false);
}
