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
    public void FtpConnectionManagerDialog_NewButtonSupportsMouseClick()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var button = currentDriver.WriteRecords.Last(r => r.Text.Contains("New", StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        var result = new FtpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Create, result.Action);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Connect", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Edit", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void FtpConnectionManagerDialog_ButtonFocusSupportsKeyboardSelection()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FtpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([TestConnection()]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Create, result.Action);
    }

    [Fact]
    public void FtpConnectionManagerDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FtpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(FtpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
    }

}
