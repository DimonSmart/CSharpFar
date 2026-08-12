using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DialogServiceTests
{
    [Fact]
    public void Select_UsesSemanticOptionsAndReturnsTheInitialSelection()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
        var dialogs = new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

        var result = dialogs.Select(new SelectionDialogOptions<string>
        {
            Title = "Pick",
            Items = ["one", "two"],
            ItemText = static item => item,
            SelectedIndex = 1,
            MaxVisibleRows = 1,
            MaxWidth = 30,
            DoubleBorder = true,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
        Assert.Equal(1, result.SelectedIndex);
    }

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
