using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class SingleLineInputDialogTests
{
    [Fact]
    public void Show_MouseClickOkConfirmsInitialText()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.BeforeReadInput = current =>
        {
            current.BeforeReadInput = null;
            FakeConsoleDriver.WriteRecord button = current.WriteRecords.Last(write =>
                write.Text.Contains("{ OK }", StringComparison.Ordinal));
            int offset = button.Text.IndexOf("{ OK }", StringComparison.Ordinal);
            int x = button.X + offset + 2;

            current.EnqueueInput(new MouseConsoleInputEvent(
                x, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(
                x, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        string? result = new SingleLineInputDialog(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(new SingleLineInputDialogOptions
            {
                Title = "Input",
                Prompt = "Name",
                InitialText = "value",
            });

        Assert.Equal("value", result);
    }
}
