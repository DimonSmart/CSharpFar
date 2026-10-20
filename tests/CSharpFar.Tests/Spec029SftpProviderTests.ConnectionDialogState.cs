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
    public void SftpConnectionDialog_ShowsCursorInFocusedInputField()
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

        var result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(
                Connection: null,
                SavedPassword: null,
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(cursorWasVisibleBeforeInput);
        Assert.Contains(driver.WriteRecords, r =>
            r.Text.Contains("Host:", StringComparison.Ordinal) &&
            r.Background == ConsoleColor.Gray);
    }

    [Fact]
    public void SftpConnectionDialog_TypingConnectionNameWithoutSaveConnectionRowDoesNotCrash()
    {
        var driver = CreateDriver();
        driver.EnqueueKey(Printable('n'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = CreateSftpDialog(
            ModalTestHost.Create(new ScreenRenderer(driver)),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
                new SftpConnectionDialogRequest(
                    Connection: TestConnection(),
                    SavedPassword: "secret-password",
                    SaveConnectionByDefault: true,
                    AllowTemporaryConnection: false),
                _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("testn", result.Connection.DisplayName);
    }

    [Fact]
    public void SftpConnectionDialog_HistoryEscapeClosesDropdownBeforeCancellingDialog()
    {
        string connectionName = "escape-history-" + Guid.NewGuid().ToString("N");
        ITextFieldHistoryProvider historyRegistry = TextFieldHistoryTestProvider.Create();
        var seedDriver = CreateDriver();
        seedDriver.EnqueueKey(Key(ConsoleKey.F10));
        var seedScreen = new ScreenRenderer(seedDriver);
        _ = CreateSftpDialog(ModalTestHost.Create(seedScreen), new FormFieldFactory(historyRegistry)).Show(
            new SftpConnectionDialogRequest(
                TestConnection() with { DisplayName = connectionName },
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        int inputReads = 0;
        Action<FakeConsoleDriver>? beforeRead = null;
        beforeRead = currentDriver =>
        {
            inputReads++;
            currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;
        driver.EnqueueKey(new ConsoleKeyInfo('e', ConsoleKey.E, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        int validationCalls = 0;

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(historyRegistry)).Show(
            new SftpConnectionDialogRequest(
                Connection: null,
                SavedPassword: null,
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ =>
            {
                validationCalls++;
                return SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.Null(result);
        Assert.Equal(4, inputReads);
        Assert.Equal(0, validationCalls);
    }

    [Fact]
    public void SftpConnectionDialog_AcceptedValidationAddsOnlyNonSecretHistory()
    {
        string host = "accepted-history-" + Guid.NewGuid().ToString("N") + ".test";
        const string password = "sftp-history-secret";
        ITextFieldHistoryProvider history = TextFieldHistoryTestProvider.Create();
        var driver = CreateDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));

        _ = CreateSftpDialog(ModalTestHost.Create(new ScreenRenderer(driver)), new FormFieldFactory(history)).Show(
            new SftpConnectionDialogRequest(TestConnection() with { Host = host }, password, true, true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Contains(host, history.Get(SftpTextHistoryIds.Host).Items);
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains(password, StringComparison.Ordinal));
        Assert.All(
            new[] { SftpTextHistoryIds.ConnectionName, SftpTextHistoryIds.Host, SftpTextHistoryIds.Port, SftpTextHistoryIds.UserName, SftpTextHistoryIds.RemoteRoot },
            id => Assert.DoesNotContain(history.Get(id).Items, item => item.Contains(password, StringComparison.Ordinal)));
    }

    [Fact]
    public void SftpConnectionDialog_RejectedValidationAndCancelDoNotAddHistory()
    {
        string rejectedHost = "rejected-history-" + Guid.NewGuid().ToString("N") + ".test";
        ITextFieldHistoryProvider history = TextFieldHistoryTestProvider.Create();
        var rejectedDriver = CreateDriver();
        rejectedDriver.EnqueueKey(Key(ConsoleKey.F10));
        rejectedDriver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = CreateSftpDialog(ModalTestHost.Create(new ScreenRenderer(rejectedDriver)), new FormFieldFactory(history)).Show(
            new SftpConnectionDialogRequest(TestConnection() with { Host = rejectedHost }, "secret-password", true, true),
            _ => SftpConnectionDialogValidationResult.Error("rejected"));

        var cancelDriver = CreateDriver();
        cancelDriver.EnqueueKey(Key(ConsoleKey.Escape));
        _ = CreateSftpDialog(ModalTestHost.Create(new ScreenRenderer(cancelDriver)), new FormFieldFactory(history)).Show(
            new SftpConnectionDialogRequest(TestConnection() with { Host = "cancelled-history.test" }, "secret-password", true, true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Empty(history.Get(SftpTextHistoryIds.Host).Items);
    }

    [Fact]
    public void SftpConnectionDialog_EnterInTextInput_SubmitsForm()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        int validationCalls = 0;

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            _ =>
            {
                validationCalls++;
                return SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal(1, validationCalls);
    }

    [Fact]
    public void SftpConnectionDialog_ChangingHostClearsHostKeyFingerprint()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { ExpectedHostKeyFingerprint = "AA:BB" }, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Null(result.Connection.ExpectedHostKeyFingerprint);
    }

    [Fact]
    public void SftpConnectionDialog_SavePasswordEnablesSaveConnection()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection(), "secret-password", SaveConnectionByDefault: false, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.True(result.SavePassword);
    }

    [Fact]
    public void SftpConnectionDialog_DisablingSaveConnectionDisablesSavePassword()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.SaveConnection);
        Assert.False(result.SavePassword);
        Assert.Null(result.Connection.CredentialId);
    }

    [Fact]
    public void SftpConnectionDialog_DisablingSavePasswordKeepsSaveConnection()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 7; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { CredentialId = "cred" }, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

    [Fact]
    public void SftpConnectionDialog_EnablingSaveConnectionDoesNotEnableSavePassword()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 6; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection(), SavedPassword: "secret-password", SaveConnectionByDefault: false, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SaveConnection);
        Assert.False(result.SavePassword);
    }

    [Fact]
    public void SftpConnectionDialog_ChangingPortClearsHostKeyFingerprint()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('3', ConsoleKey.D3, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { ExpectedHostKeyFingerprint = "AA:BB" }, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Null(result.Connection.ExpectedHostKeyFingerprint);
    }

    [Fact]
    public void SftpConnectionDialog_ValidationErrorKeepsDialogOpenAndPreservesInput()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var candidates = new List<SftpConnectionDialogResult>();

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection(), "secret-password", true, true),
            candidate =>
            {
                candidates.Add(candidate);
                return candidates.Count == 1 ? SftpConnectionDialogValidationResult.Error("invalid") : SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.Equal(2, candidates.Count);
        Assert.Equal("example.testx", candidates[0].Connection.Host);
        Assert.Equal(candidates[0].Connection.Host, candidates[1].Connection.Host);
        Assert.Equal("example.testx", result!.Connection.Host);
    }

    [Fact]
    public void SftpConnectionDialog_HostKeyReviewRebuildPreservesTextState()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { ExpectedHostKeyFingerprint = null }, "secret-password", true, true),
            candidate => candidate.Connection.ExpectedHostKeyFingerprint is null
                ? SftpConnectionDialogValidationResult.RequireHostKeyTrust("AA:BB")
                : SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("example.testx", result.Connection.Host);
        Assert.Equal("AA:BB", result.Connection.ExpectedHostKeyFingerprint);
    }

    [Fact]
    public void SftpConnectionDialog_EndpointChangeDuringHostKeyReviewClearsFingerprintAndTrust()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Home));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var candidates = new List<SftpConnectionDialogResult>();

        _ = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection() with { ExpectedHostKeyFingerprint = null }, "secret-password", true, true),
            candidate =>
            {
                candidates.Add(candidate);
                return candidates.Count == 1
                    ? SftpConnectionDialogValidationResult.RequireHostKeyTrust("AA:BB")
                    : SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.Equal(2, candidates.Count);
        Assert.Null(candidates[0].Connection.ExpectedHostKeyFingerprint);
        Assert.Null(candidates[1].Connection.ExpectedHostKeyFingerprint);
        Assert.Equal("example.testx", candidates[1].Connection.Host);
    }

}
