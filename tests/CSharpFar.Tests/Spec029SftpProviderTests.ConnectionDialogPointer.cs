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
    public void SftpConnectionDialog_MouseClickFocusesTextField()
    {
        var driver = CreateDriver();
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
                    currentDriver.CursorY == 9;
            }

            if (readCount < 2)
                currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;
        driver.EnqueueInput(LeftMouse(40, 9));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(hostFieldFocused);
    }

    [Fact]
    public void SftpConnectionDialog_MouseClickTogglesCheckboxField()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            currentDriver.BeforeReadInput = null;
            var row = currentDriver.WriteRecords.Last(r => r.Text.Contains("Save password", StringComparison.Ordinal));
            currentDriver.EnqueueInput(LeftMouse(row.X + 1, row.Y));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.True(result.SavePassword);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Show in drive menu", StringComparison.Ordinal));
    }

    [Fact]
    public void SftpConnectionDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("test", result.Connection.DisplayName);
    }

    [Fact]
    public void SftpConnectionDialog_ShortConsoleRendersBodyScrollbar()
    {
        var driver = CreateDriver(height: 12);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, r => r.Text == "▲");
        Assert.Contains(driver.WriteRecords, r => r.Text == "▼");
    }

    [Fact]
    public void SftpConnectionDialog_RebuildsHostKeyRowsAndFocusesTrustAfterValidation()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        int validationCalls = 0;
        var connection = TestConnection() with { ExpectedHostKeyFingerprint = null };

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(connection, "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            candidate =>
            {
                validationCalls++;
                return candidate.Connection.ExpectedHostKeyFingerprint is null
                    ? SftpConnectionDialogValidationResult.RequireHostKeyTrust("AA:BB")
                    : SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.Equal(2, validationCalls);
        Assert.NotNull(result);
        Assert.Equal("AA:BB", result.Connection.ExpectedHostKeyFingerprint);
    }

    [Fact]
    public void SftpConnectionDialog_ResizePreservesInputAndContinuesInteraction()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        int readCount = 0;
        bool cursorWasInHostInputAfterResize = false;
        string cursorDiagnostic = string.Empty;
        ConsoleInputEvent[] inputs =
        [
            new KeyConsoleInputEvent(Key(ConsoleKey.Tab)),
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true)),
            new KeyConsoleInputEvent(Printable('n')),
            new KeyConsoleInputEvent(Printable('e')),
            new KeyConsoleInputEvent(Printable('w')),
            new ConsoleResizeInputEvent(),
            new KeyConsoleInputEvent(Printable('-')),
            new KeyConsoleInputEvent(Printable('h')),
            new KeyConsoleInputEvent(Printable('o')),
            new KeyConsoleInputEvent(Printable('s')),
            new KeyConsoleInputEvent(Printable('t')),
            new KeyConsoleInputEvent(Key(ConsoleKey.F10)),
        ];
        Action<FakeConsoleDriver>? beforeRead = null;
        beforeRead = currentDriver =>
        {
            if (readCount == 5)
                currentDriver.SetSize(80, 24);
            if (readCount == 6)
                cursorWasInHostInputAfterResize = IsCursorInsideInputForLabel(currentDriver, "Host:", out cursorDiagnostic);
            currentDriver.EnqueueInput(inputs[readCount]);
            readCount++;
            if (readCount < inputs.Length)
                currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;
        var candidates = new List<SftpConnectionDialogResult>();

        SftpConnectionDialogResult? result = CreateSftpDialog(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(
            new SftpConnectionDialogRequest(TestConnection(), "secret-password", SaveConnectionByDefault: true, AllowTemporaryConnection: true),
            candidate =>
            {
                candidates.Add(candidate);
                return SftpConnectionDialogValidationResult.Accepted();
            });

        Assert.NotNull(result);
        Assert.Equal("new-host", result.Connection.Host);
        Assert.Equal("new-host", Assert.Single(candidates).Connection.Host);
        Assert.True(cursorWasInHostInputAfterResize, cursorDiagnostic);
    }

}
