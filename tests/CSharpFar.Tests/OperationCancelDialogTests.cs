using CSharpFar.App.Dialogs;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class OperationCancelDialogTests
{
    [Fact]
    public void NoButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.BeforeReadInput = current =>
        {
            var record = current.WriteRecords.Last(value => value.Text.Contains("[ No ]", StringComparison.Ordinal));
            int x = record.X + record.Text.IndexOf("No", StringComparison.Ordinal);
            current.EnqueueInput(new MouseConsoleInputEvent(
                x,
                record.Y,
                MouseButton.Left,
                MouseEventKind.Down,
                MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(
                x,
                record.Y,
                MouseButton.Left,
                MouseEventKind.Up,
                MouseKeyModifiers.None));
        };

        bool result = Show(driver);

        Assert.False(result);
    }

    [Fact]
    public void UsesStandardDialogPalette()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(new ConsoleKeyInfo('N', ConsoleKey.N, shift: false, alt: false, control: false));

        _ = Show(driver);

        var messageRecord = driver.WriteRecords.Last(value =>
            value.Text.Contains("Operation has been interrupted", StringComparison.Ordinal));
        Assert.Equal(UiTheme.Current.DialogBackground, messageRecord.Background);
        Assert.NotEqual(UiTheme.Current.WarningBackground, messageRecord.Background);
    }

    private static bool Show(FakeConsoleDriver driver)
    {
        var modals = ModalTestHost.Create(driver);
        return new OperationCancelDialog(
            new DialogService(modals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show();
    }
}
