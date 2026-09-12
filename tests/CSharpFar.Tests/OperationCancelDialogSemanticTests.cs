using CSharpFar.App.Dialogs;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class OperationCancelDialogSemanticTests
{
    [Theory]
    [InlineData(ConsoleKey.Enter, true)]
    [InlineData(ConsoleKey.Y, true)]
    [InlineData(ConsoleKey.N, false)]
    [InlineData(ConsoleKey.Escape, false)]
    public void StandardChoiceKeysReturnExpectedResult(ConsoleKey key, bool expected)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        char keyChar = key is ConsoleKey.Y ? 'y' : key is ConsoleKey.N ? 'n' : '\0';
        driver.EnqueueKey(new ConsoleKeyInfo(keyChar, key, false, false, false));

        bool result = Show(driver);

        Assert.Equal(expected, result);
    }

    private static bool Show(FakeConsoleDriver driver)
    {
        var modals = ModalTestHost.Create(driver);
        return new OperationCancelDialog(
            new DialogService(modals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show();
    }
}
