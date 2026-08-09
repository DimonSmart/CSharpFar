using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DialogServiceTests
{
    [Fact]
    public void Form_DelegatesToTheOrdinaryFormFacade()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        var dialogs = new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

        string? result = dialogs.Form(
            new FormDialogOptions("Service", 30, 8),
            rows: () => [FormControls.Label("Body")],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<string?>.Complete(null)
                : FormDialogOutcome<string?>.Continue());

        Assert.Null(result);
    }
}
