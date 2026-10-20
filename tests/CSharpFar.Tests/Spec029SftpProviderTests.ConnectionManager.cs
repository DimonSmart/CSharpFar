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
    public void SftpConnectionManagerDialog_NewButtonSupportsMouseClick()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var button = currentDriver.WriteRecords.Last(r => r.Text.Contains("New", StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        var result = new SftpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Create, result.Action);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Connect", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Edit", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void SftpConnectionManagerDialog_DoubleClickConnectsSelectedConnection()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(r => r.Text.Contains(connection.DisplayName, StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(row.X + 1, row.Y, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None));
        };

        var result = new SftpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
    }

    [Fact]
    public void SftpConnectionManagerDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = CreateDriver();
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SftpConnectionManagerDialog(CreateDialogs(ModalTestHost.Create(screen))).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
    }



}
